using System.Collections;
using System.Data;
using System.Reflection;
using System.Text.Json.Nodes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// TYPE-002: every column type in <see cref="DatabaseTypeCatalog"/> that has a CLR type is
/// round-tripped against the real database: declared as a column, written through
/// <see cref="TableGateway{TEntity,TRowID}"/> with its natural DbType, read back through the
/// gateway and through <see cref="DataReaderMapper"/>, and (where the type supports equality)
/// matched with <c>WHERE v = @p</c>. Then every other write path writes it again, each from a NULL
/// the write must replace: UpdateAsync, both UpsertAsync branches, BatchCreateAsync and
/// BatchUpdateAsync; NULL itself is read back and (for reference types) written. Every failure on a
/// database is collected and reported together, so one run shows the whole picture.
/// </summary>
[Collection("IntegrationTests")]
public class TypeRoundTripMatrixTests : DatabaseTestBase
{
    private const string TableName = "type_rt";

    public TypeRoundTripMatrixTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) =>
        Task.CompletedTask;

    [SkippableFact]
    public async Task EveryCatalogType_RoundTripsThroughTheGatewayAndDataReaderMapper()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var entries = DatabaseTypeCatalog.GetColumnTypes(provider)
                .Where(e => e.ClrType != null && e.DbType != null && e.CanDeclareColumn)
                .ToList();
            // REV-062: an empty catalog used to pass silently; every database has one now.
            Assert.True(entries.Count > 0, $"{provider}: no catalog entries with a CLR type");

            var failures = new List<string>();
            foreach (var entry in entries)
            {
                if (entry.Setup != null)
                {
                    // Before the type's own context: Npgsql loads a data source's type catalog once,
                    // so an extension or user-defined type must exist before that context is created.
                    try
                    {
                        await using var setup = context.CreateSqlContainer(entry.Setup);
                        await setup.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{entry.Declaration ?? entry.CanonicalName}: setup: {Describe(ex)}");
                        continue;
                    }
                }

                // A fresh context (and pool) per type: the table is recreated with a different
                // column type each time, and a pooled connection's prepared plans for the same SQL
                // text would otherwise fail with "cached plan must not change result type".
                await using var typeContext = await CreateAdditionalContextAsync(provider);
                var failure = await RoundTripAsync(provider, typeContext, entry);
                if (failure != null)
                {
                    failures.Add($"{entry.Declaration ?? entry.CanonicalName}: {failure}");
                }

                // Fractional seconds a column can't hold are truncated, never rounded: one tick before
                // midnight must not read back as the next day (docs/utc-and-time.md).
                if (EdgeSample(entry) is { } edgeEntry)
                {
                    await using var edgeContext = await CreateAdditionalContextAsync(provider);
                    var edgeFailure = await EdgeRoundTripAsync(provider, edgeContext, edgeEntry);
                    if (edgeFailure != null)
                    {
                        failures.Add($"{entry.Declaration ?? entry.CanonicalName} (one tick before midnight): {edgeFailure}");
                    }
                }

                // A DateTimeOffset written to a column with no offset stores its UTC instant: drivers
                // given the DateTimeOffset itself stored its local wall time, two hours off for +02:00.
                // An Unspecified DateTime is UTC on every path: Npgsql refused one for timestamptz.
                foreach (var (label, variant) in new[]
                         {
                             ("DateTimeOffset +02:00", AsOffsetValue(entry)),
                             ("DateTime Kind=Unspecified", AsUnspecifiedValue(entry))
                         })
                {
                    if (variant == null)
                    {
                        continue;
                    }

                    await using var variantContext = await CreateAdditionalContextAsync(provider);
                    var variantFailure = await RoundTripAsync(provider, variantContext, variant);
                    if (variantFailure != null)
                    {
                        failures.Add($"{entry.Declaration ?? entry.CanonicalName} ({label}): {variantFailure}");
                    }
                }
            }

            Output.WriteLine($"{provider}: {entries.Count - failures.Count}/{entries.Count} types round-tripped");
            foreach (var f in failures)
            {
                Output.WriteLine("  FAIL " + f);
            }

            Assert.True(failures.Count == 0,
                $"{provider}: {failures.Count} of {entries.Count} types failed:\n" + string.Join("\n", failures));
        });
    }

    // The latest value of the entry's type before a whole second, minute and day: a column that rounds
    // its extra digits carries it into the next day.
    private static ColumnTypeDescriptor? EdgeSample(ColumnTypeDescriptor entry)
    {
        var lastTick = TimeSpan.FromDays(1) - TimeSpan.FromTicks(1);
        object? edge = entry.Sample switch
        {
            DateTime when entry.DbType is DbType.DateTime or DbType.DateTime2 => new DateTime(2026, 12, 31).Add(lastTick),
            DateTimeOffset when entry.DbType == DbType.DateTimeOffset =>
                new DateTimeOffset(new DateTime(2026, 12, 31).Add(lastTick), TimeSpan.Zero),
            TimeSpan => lastTick,
            TimeOnly => TimeOnly.FromTimeSpan(lastTick),
            pengdows.crud.types.valueobjects.IntervalDaySecond => new pengdows.crud.types.valueobjects.IntervalDaySecond(1, lastTick),
            _ => null
        };
        return edge == null || entry.IsAutoGenerated ? null : entry with { Sample = edge, Comparable = false };
    }

    private static long? EdgeTicks(object? value) => value switch
    {
        DateTime dt => dt.Ticks,
        DateTimeOffset dto => dto.UtcTicks,
        TimeSpan ts => ts.Ticks,
        TimeOnly t => t.Ticks,
        pengdows.crud.types.valueobjects.IntervalDaySecond i => i.TotalTime.Ticks,
        _ => null
    };

    private async Task<string?> EdgeRoundTripAsync(SupportedDatabase provider, IDatabaseContext context,
        ColumnTypeDescriptor entry)
    {
        var declare = await DeclareAsync(provider, context, entry);
        if (declare != null)
        {
            return declare;
        }

        var rowType = TypeRoundTripRows.ByDbType[entry.DbType!.Value].MakeGenericType(entry.ClrType!);
        var method = typeof(TypeRoundTripMatrixTests)
            .GetMethod(nameof(EdgeRowAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(rowType);
        return await (Task<string?>)method.Invoke(null, new object[] { context, entry })!;
    }

    private static async Task<string?> EdgeRowAsync<TRow>(IDatabaseContext context, ColumnTypeDescriptor entry)
        where TRow : class, new()
    {
        var idProperty = typeof(TRow).GetProperty("Id")!;
        var valueProperty = typeof(TRow).GetProperty("V")!;
        var row = new TRow();
        idProperty.SetValue(row, 1);
        valueProperty.SetValue(row, entry.Sample);
        var gateway = new TableGateway<TRow, int>(context);
        try
        {
            await gateway.CreateAsync(row, context);
            var loaded = await gateway.RetrieveOneAsync(1, context);
            var read = loaded == null ? null : valueProperty.GetValue(loaded);
            return EdgeTicks(read) is { } readTicks && readTicks <= EdgeTicks(entry.Sample)
                ? null
                : $"rounded up: wrote {Show(entry.Sample)}, read {Show(read)}";
        }
        catch (Exception ex)
        {
            return Describe(ex);
        }
    }

    private static async Task<string?> DeclareAsync(SupportedDatabase provider, IDatabaseContext context,
        ColumnTypeDescriptor entry)
    {
        try
        {
            await DropTableIfExistsAsync(context, TableName);
            await using (var create = context.CreateSqlContainer(
                             $"CREATE {TableKind(provider)}TABLE {IntegrationObjectNameHelper.Table(context, TableName)} (" +
                             $"{context.WrapObjectName("id")} {IntegrationObjectNameHelper.IntType(provider)} NOT NULL PRIMARY KEY, " +
                             $"{context.WrapObjectName("v")} {entry.Declaration}{NullableSuffix(provider, entry.Declaration!)}){TableSuffix(provider)}"))
            {
                await create.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            return "declare: " + Describe(ex);
        }

        return null;
    }

    private static ColumnTypeDescriptor? AsUnspecifiedValue(ColumnTypeDescriptor entry) =>
        entry is { Sample: DateTime { Kind: DateTimeKind.Utc } sample }
            ? entry with { Sample = DateTime.SpecifyKind(sample, DateTimeKind.Unspecified) }
            : null;

    private static ColumnTypeDescriptor? AsOffsetValue(ColumnTypeDescriptor entry) =>
        entry is { ClrType: var clr, DbType: DbType.DateTime or DbType.DateTime2, Sample: DateTime sample } &&
        clr == typeof(DateTime)
            ? entry with
            {
                ClrType = typeof(DateTimeOffset),
                Sample = new DateTimeOffset(DateTime.SpecifyKind(sample, DateTimeKind.Utc)).ToOffset(TimeSpan.FromHours(2))
            }
            : null;

    private async Task<string?> RoundTripAsync(SupportedDatabase provider, IDatabaseContext context,
        ColumnTypeDescriptor entry)
    {
        var declare = await DeclareAsync(provider, context, entry);
        if (declare != null)
        {
            return declare;
        }

        var rowType = TypeRoundTripRows.ByDbType[entry.DbType!.Value].MakeGenericType(entry.ClrType!);
        var method = typeof(TypeRoundTripMatrixTests)
            .GetMethod(nameof(RoundTripRowAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(rowType, entry.ClrType!);
        return await (Task<string?>)method.Invoke(this, new object[] { context, entry })!;
    }

    private async Task<string?> RoundTripRowAsync<TRow, T>(IDatabaseContext context, ColumnTypeDescriptor entry)
        where TRow : class, new()
    {
        var idProperty = typeof(TRow).GetProperty("Id")!;
        var valueProperty = typeof(TRow).GetProperty("V")!;
        var row = new TRow();
        idProperty.SetValue(row, 1);
        valueProperty.SetValue(row, entry.Sample);
        var gateway = new TableGateway<TRow, int>(context);

        try
        {
            await gateway.CreateAsync(row, context);
        }
        catch (Exception ex)
        {
            return "write: " + Describe(ex);
        }

        try
        {
            var loaded = await gateway.RetrieveOneAsync(1, context);
            if (loaded == null)
            {
                return "gateway read: row not found";
            }

            var actual = valueProperty.GetValue(loaded);
            if (!SameValue(entry.Sample, actual, Provider(context)))
            {
                return $"gateway read: wrote {Show(entry.Sample)}, read {Show(actual)}";
            }
        }
        catch (Exception ex)
        {
            return "gateway read: " + Describe(ex);
        }

        try
        {
            await using var select = context.CreateSqlContainer();
            // Custom SQL selects what the gateway selects: some columns are read through a conversion
            // (Oracle SDO_GEOMETRY as EWKT, TYPE-021), the column itself everywhere else.
            var column = ((pengdows.crud.@internal.ITypeMapAccessor)context).TypeMapRegistry.GetTableInfo<TRow>().Columns["v"];
            var reference = context.WrapObjectName("v");
            var selected = ((pengdows.crud.dialects.SqlDialect)context.Dialect)
                .RenderColumnSelect(reference, context.WrapObjectName("V"), column);
            if (selected == reference)
            {
                selected = string.Concat(reference, " AS ", context.WrapObjectName("V"));
            }

            select.Query.Append("SELECT ").Append(selected).Append(" FROM ")
                .Append(IntegrationObjectNameHelper.Table(context, TableName));
            await using var reader = await select.ExecuteReaderAsync();
            var mapped = await DataReaderMapper.LoadObjectsFromDataReaderAsync<MappedValue<T>>(reader);
            if (mapped.Count != 1 || !SameValue(entry.Sample, mapped[0].V, Provider(context)))
            {
                return mapped.Count == 1
                    ? $"DataReaderMapper: wrote {Show(entry.Sample)}, read {Show(mapped[0].V)} ({select.Query})"
                    : $"DataReaderMapper: {mapped.Count} rows ({select.Query})";
            }
        }
        catch (Exception ex)
        {
            return "DataReaderMapper: " + Describe(ex);
        }

        if (entry.Comparable)
        {
            var whereFailure = await WhereMatchesAsync(context, entry);
            if (whereFailure != null)
            {
                return whereFailure;
            }
        }

        return await WritePathsAsync<TRow>(context, entry, gateway, idProperty, valueProperty);
    }

    private static async Task<string?> WhereMatchesAsync(IDatabaseContext context, ColumnTypeDescriptor entry)
    {
        try
        {
            await using var where = context.CreateSqlContainer();
            where.Query.Append("SELECT COUNT(*) FROM ").Append(IntegrationObjectNameHelper.Table(context, TableName))
                .Append(" WHERE ").Append(context.WrapObjectName("v")).Append(" = ")
                .Append(where.MakeParameterName("p"));
            where.AddParameterWithValue("p", entry.DbType!.Value, entry.Sample);
            var count = await where.ExecuteScalarRequiredAsync<long>();
            if (count != 1)
            {
                return $"WHERE v = @p matched {count} rows";
            }
        }
        catch (Exception ex)
        {
            return "WHERE: " + Describe(ex);
        }

        return null;
    }

    // Each write starts from a NULL it must replace (set by plain SQL), so a write that silently
    // changes nothing can't pass. A column declared NOT NULL (ASE BIT) skips the NULL steps; a
    // value-type property can't hold NULL, so only reference types read and write it.
    // Each step sets up the rows it needs, so a failed step can't fail the ones after it.
    private async Task<string?> WritePathsAsync<TRow>(IDatabaseContext context, ColumnTypeDescriptor entry,
        TableGateway<TRow, int> gateway, PropertyInfo idProperty, PropertyInfo valueProperty)
        where TRow : class, new()
    {
        // A generated column (SERIAL) is implicitly NOT NULL, and Informix refuses to update one,
        // so it is only inserted and read (WRT-002).
        var canHoldNull = !entry.IsAutoGenerated &&
                          !entry.Declaration!.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase);
        var canUpdate = !entry.IsAutoGenerated;
        // InterBase has no upsert statement at all (WRT-003): a capability, not a failure.
        var info = context.DataSourceInfo;
        var canUpsert = info.SupportsMerge || info.SupportsInsertOnConflict || info.SupportsOnDuplicateKey;
        var nullableProperty = !valueProperty.PropertyType.IsValueType ||
                               Nullable.GetUnderlyingType(valueProperty.PropertyType) != null;
        var provider = Provider(context);

        TRow Row(int id, object? value)
        {
            var row = new TRow();
            idProperty.SetValue(row, id);
            valueProperty.SetValue(row, value);
            return row;
        }

        async Task SetNull(IDatabaseContext ctx, params int[] ids)
        {
            if (!canHoldNull)
            {
                return;
            }

            foreach (var id in ids)
            {
                await using var sc = ctx.CreateSqlContainer();
                sc.Query.Append("UPDATE ").Append(IntegrationObjectNameHelper.Table(ctx, TableName))
                    .Append(" SET ").Append(ctx.WrapObjectName("v")).Append(" = NULL WHERE ")
                    .Append(ctx.WrapObjectName("id")).Append(" = ").Append(sc.MakeParameterName("id"));
                sc.AddParameterWithValue("id", DbType.Int32, id);
                await sc.ExecuteNonQueryAsync();
            }
        }

        async Task<string?> Expect(IDatabaseContext ctx, string phase, object? expected, params int[] ids)
        {
            foreach (var id in ids)
            {
                var loaded = await gateway.RetrieveOneAsync(id, ctx);
                if (loaded == null)
                {
                    return $"{phase}: row {id} not found";
                }

                var actual = valueProperty.GetValue(loaded);
                if (!SameValue(expected, actual, provider))
                {
                    return $"{phase}: wrote {Show(expected)}, read {Show(actual)}";
                }
            }

            return null;
        }

        async Task<string?> Phase(string phase, Func<IDatabaseContext, Task<string?>> body)
        {
            try
            {
                return await body(context);
            }
            catch (Exception ex)
            {
                return $"{phase}: {Describe(ex)}";
            }
        }

        var steps = new (string Name, bool Applies, Func<IDatabaseContext, Task<string?>> Body)[]
        {
            ("null read", canHoldNull && nullableProperty, async ctx =>
            {
                await SetNull(ctx, 1);
                return await Expect(ctx, "null read", null, 1);
            }),
            ("update", canUpdate, async ctx =>
            {
                await SetNull(ctx, 1);
                await gateway.UpdateAsync(Row(1, entry.Sample), ctx);
                return await Expect(ctx, "update", entry.Sample, 1);
            }),
            ("upsert (existing row)", canUpsert && canUpdate, async ctx =>
            {
                await SetNull(ctx, 1);
                await gateway.UpsertAsync(Row(1, entry.Sample), ctx);
                return await Expect(ctx, "upsert (existing row)", entry.Sample, 1);
            }),
            // An upsert statement carries an update branch, which Informix refuses for a SERIAL.
            ("upsert (new row)", canUpsert && canUpdate, async ctx =>
            {
                await gateway.UpsertAsync(Row(2, entry.Sample), ctx);
                return await Expect(ctx, "upsert (new row)", entry.Sample, 2);
            }),
            ("batch create", true, async ctx =>
            {
                await gateway.BatchCreateAsync(new[] { Row(3, entry.Sample), Row(4, entry.Sample) }, ctx);
                return await Expect(ctx, "batch create", entry.Sample, 3, 4);
            }),
            ("batch update", canUpdate, async ctx =>
            {
                await gateway.CreateAsync(Row(6, entry.Sample), ctx);
                await gateway.CreateAsync(Row(7, entry.Sample), ctx);
                await SetNull(ctx, 6, 7);
                await gateway.BatchUpdateAsync(new[] { Row(6, entry.Sample), Row(7, entry.Sample) }, ctx);
                return await Expect(ctx, "batch update", entry.Sample, 6, 7);
            }),
            ("null write", canHoldNull && nullableProperty, async ctx =>
            {
                await gateway.CreateAsync(Row(5, null), ctx);
                return await Expect(ctx, "null write", null, 5);
            }),
        };

        // Every failing step is reported, not only the first, so one fix doesn't uncover the next.
        var failures = new List<string>();
        foreach (var (name, applies, body) in steps)
        {
            if (!applies)
            {
                continue;
            }

            var failure = await Phase(name, body);
            if (failure != null)
            {
                failures.Add(failure);
            }
        }

        return failures.Count == 0 ? null : string.Join(" || ", failures);
    }

    // Sybase ASE columns are NOT NULL unless declared NULL; every other database defaults to
    // nullable and some (Firebird, Informix) reject an explicit NULL constraint.
    // A declaration that states its own nullability (ASE BIT can't be NULL) is left as is.
    private static string NullableSuffix(SupportedDatabase provider, string declaration) =>
        provider == SupportedDatabase.SybaseASE && !declaration.Contains("NULL", StringComparison.OrdinalIgnoreCase)
            ? " NULL"
            : string.Empty;

    // SingleStore's default columnar tables can't hold GEOGRAPHY; a rowstore table holds every type.
    private static string TableKind(SupportedDatabase provider) =>
        provider == SupportedDatabase.SingleStore ? "ROWSTORE " : string.Empty;

    // Oracle's test user's default tablespace is SYSTEM, where JSON/XMLTYPE/VECTOR/SecureFiles LOBs
    // can't be created (ORA-43853); VectorRoundTripTests does the same. A FlatFile (CSV) field tells
    // NULL from '' only with a NULLTOKEN (DRY-024), as the other FlatFile tables declare.
    private static string TableSuffix(SupportedDatabase provider) => provider switch
    {
        SupportedDatabase.Oracle => " TABLESPACE USERS",
        SupportedDatabase.FlatFile => " WITH (NULLTOKEN = '<<NULL>>')",
        _ => string.Empty
    };

    private static SupportedDatabase Provider(IDatabaseContext context) => context.Product;

    private static bool SameValue(object? expected, object? actual, SupportedDatabase provider)
    {
        // SingleStore's GEOGRAPHYPOINT stores coordinates to about 1e-7 degrees (lossy by design,
        // confirmed live: POINT(0 0) reads back as 3.6e-8); compare points within that.
        if (provider == SupportedDatabase.SingleStore &&
            expected is pengdows.crud.types.valueobjects.SpatialValue p1 && actual is pengdows.crud.types.valueobjects.SpatialValue p2 &&
            SpatialWkb(p1) is { Length: 21 } wkb1 && SpatialWkb(p2) is { Length: 21 } wkb2 && wkb1[1] == 1 && wkb2[1] == 1)
        {
            return p1.Srid == p2.Srid &&
                   Math.Abs(BitConverter.ToDouble(wkb1, 5) - BitConverter.ToDouble(wkb2, 5)) < 1e-6 &&
                   Math.Abs(BitConverter.ToDouble(wkb1, 13) - BitConverter.ToDouble(wkb2, 13)) < 1e-6;
        }

        switch (expected)
        {
            case null:
                return actual is null or DBNull;
            case byte[] bytes:
                return actual is byte[] other && bytes.AsSpan().SequenceEqual(other);
            case DateTime dt:
                return actual is DateTime a && a.Ticks == dt.Ticks;
            case DateTimeOffset dto:
                return actual is DateTimeOffset b && b.UtcTicks == dto.UtcTicks;
            case BitArray bits:
                return actual is BitArray readBits && bits.Length == readBits.Length &&
                       Enumerable.Range(0, bits.Length).All(i => bits[i] == readBits[i]);
            case pengdows.crud.types.valueobjects.SpatialValue spatial:
                return actual is pengdows.crud.types.valueobjects.SpatialValue read &&
                       read.GetType() == spatial.GetType() && read.Srid == spatial.Srid &&
                       SpatialWkb(spatial).AsSpan().SequenceEqual(SpatialWkb(read));
            case pengdows.crud.types.valueobjects.JsonValue json:
                return actual is pengdows.crud.types.valueobjects.JsonValue j &&
                       JsonNode.DeepEquals(JsonNode.Parse(json.AsString()), JsonNode.Parse(j.AsString()));
            // XML columns may reformat the document (Oracle XMLTYPE adds whitespace): compare as XML.
            case string xml when xml.TrimStart().StartsWith('<') && actual is string readXml:
                return System.Xml.Linq.XNode.DeepEquals(System.Xml.Linq.XElement.Parse(xml),
                    System.Xml.Linq.XElement.Parse(readXml.Trim()));
            case string s when s.TrimStart().StartsWith('{') && actual is string t:
                return JsonNode.DeepEquals(JsonNode.Parse(s), JsonNode.Parse(t));
            case IStructuralEquatable structural when actual != null:
                return structural.Equals(actual, StructuralComparisons.StructuralEqualityComparer);
            default:
                return Equals(expected, actual);
        }
    }

    // A spatial value built from WKT and one read back as WKB are the same shape when their WKB is.
    private static byte[] SpatialWkb(pengdows.crud.types.valueobjects.SpatialValue value) =>
        value.WellKnownBinary.IsEmpty
            ? pengdows.crud.types.converters.WellKnownTextEncoder.Encode(value.WellKnownText!)
            : value.WellKnownBinary.ToArray();

    private static string Show(object? value) => value switch
    {
        pengdows.crud.types.valueobjects.SpatialValue s => $"SRID {s.Srid} 0x{Convert.ToHexString(SpatialWkb(s))}",
        null => "null",
        string text => $"[{text}] ({text.Length} chars)",
        byte[] b => "0x" + Convert.ToHexString(b),
        IEnumerable e and not string => "[" + string.Join(", ", e.Cast<object?>()) + "]",
        DateTime d => d.ToString("O"),
        TimeOnly time => time.ToString("HH:mm:ss.fffffff"),
        TimeSpan span => span.ToString("c"),
        DateTimeOffset d => d.ToString("O"),
        _ => $"{value} ({value.GetType().Name})"
    };

    private static string Describe(Exception ex)
    {
        Exception root = ex;
        while (root is TargetInvocationException { InnerException: not null } tie)
        {
            root = tie.InnerException!;
        }

        var message = (root.Message ?? string.Empty).Replace('\n', ' ');
        var described = $"{root.GetType().Name}: {(message.Length > 220 ? message[..220] + "…" : message)}";
        if (Environment.GetEnvironmentVariable("TYPE_MATRIX_STACKS") != "1")
        {
            return described;
        }

        // Diagnostics: the innermost exception's top pengdows frames.
        var innermost = ex;
        while (innermost.InnerException != null)
        {
            innermost = innermost.InnerException;
        }

        var frames = (innermost.StackTrace ?? string.Empty).Split('\n')
            .Where(f => f.Contains("pengdows.crud.", StringComparison.Ordinal) && !f.Contains("IntegrationTests"))
            .Take(4)
            .Select(f => f.Trim());
        return $"{described} [{innermost.GetType().Name}: {innermost.Message.Split('\n')[0]} | {string.Join(" | ", frames)}]";
    }

    private sealed class MappedValue<T>
    {
        public T V { get; set; } = default!;
    }
}
