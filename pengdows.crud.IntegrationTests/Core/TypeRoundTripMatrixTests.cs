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
/// matched with <c>WHERE v = @p</c>. Every failure on a database is collected and reported
/// together, so one run shows the whole picture.
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
            if (entries.Count == 0)
            {
                Output.WriteLine($"{provider}: no catalog entries with a CLR type yet");
                return;
            }

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

    private async Task<string?> RoundTripAsync(SupportedDatabase provider, IDatabaseContext context,
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
            select.Query.Append("SELECT ").Append(context.WrapObjectName("v")).Append(" AS ")
                .Append(context.WrapObjectName("V")).Append(" FROM ")
                .Append(IntegrationObjectNameHelper.Table(context, TableName));
            await using var reader = await select.ExecuteReaderAsync();
            var mapped = await DataReaderMapper.LoadObjectsFromDataReaderAsync<MappedValue<T>>(reader);
            if (mapped.Count != 1 || !SameValue(entry.Sample, mapped[0].V, Provider(context)))
            {
                return $"DataReaderMapper: wrote {Show(entry.Sample)}, read {Show(mapped.Count == 1 ? mapped[0].V : null)}";
            }
        }
        catch (Exception ex)
        {
            return "DataReaderMapper: " + Describe(ex);
        }

        if (!entry.Comparable)
        {
            return null;
        }

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
    // can't be created (ORA-43853); VectorRoundTripTests does the same.
    private static string TableSuffix(SupportedDatabase provider) =>
        provider == SupportedDatabase.Oracle ? " TABLESPACE USERS" : string.Empty;

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
