using System.Data;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// A <see cref="Guid"/> property must work without the application knowing how the database stores
/// it. Every Guid column <see cref="DatabaseTypeCatalog"/> declares for a database (native uuid,
/// text, 16-byte binary) is written with a recognisable value through the gateway, read back through
/// the gateway and <see cref="DataReaderMapper"/>, and the bytes the database actually holds are
/// read back with plain SQL: 16-byte binary columns must hold RFC 4122 order (the order every
/// uuid tool prints) except on SQL Server, Oracle and Sybase ASE, whose drivers use .NET's
/// mixed-endian <c>ToByteArray()</c> order. Bytes laid down in the database's own order by other
/// software must read back as the same Guid.
/// </summary>
[Collection("IntegrationTests")]
public class GuidRoundTripTests : DatabaseTestBase
{
    private const string TableName = "type_rt";

    // Every byte differs and the first three groups are not palindromic, so a byte-order mixup shows.
    private static readonly Guid Probe = new("00112233-4455-6677-8899-aabbccddeeff");

    public GuidRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) =>
        Task.CompletedTask;

    [SkippableFact]
    public async Task EveryGuidColumn_RoundTripsAndStoresTheExpectedBytes()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var entries = DatabaseTypeCatalog.GetColumnTypes(provider)
                .Where(e => e.ClrType == typeof(Guid) && e.DbType is DbType.Guid or DbType.Binary && e.CanDeclareColumn)
                .ToList();
            Assert.True(entries.Count > 0, $"{provider}: the catalog declares no Guid column");

            var failures = new List<string>();
            foreach (var entry in entries)
            {
                await using var entryContext = await CreateAdditionalContextAsync(provider);
                var failure = entry.DbType == DbType.Binary
                    ? await CheckAsync<TypeRowBinary<Guid>>(provider, entryContext, entry)
                    : await CheckAsync<TypeRowGuid<Guid>>(provider, entryContext, entry);
                if (failure != null)
                {
                    failures.Add($"{entry.Declaration} ({entry.DbType}): {failure}");
                }
            }

            Assert.True(failures.Count == 0,
                $"{provider}: Guid columns that did not round-trip:{Environment.NewLine}" +
                string.Join(Environment.NewLine, failures));
        });
    }

    private static async Task<string?> CheckAsync<TRow>(SupportedDatabase provider, IDatabaseContext context,
        ColumnTypeDescriptor entry)
        where TRow : class, new()
    {
        var idProperty = typeof(TRow).GetProperty("Id")!;
        var valueProperty = typeof(TRow).GetProperty("V")!;
        try
        {
            await DropTableIfExistsAsync(context, TableName);
            var kind = provider == SupportedDatabase.SingleStore ? "ROWSTORE " : string.Empty;
            var nullable = provider == SupportedDatabase.SybaseASE ? " NULL" : string.Empty;
            var suffix = provider switch
            {
                SupportedDatabase.Oracle => " TABLESPACE USERS",
                SupportedDatabase.FlatFile => " WITH (NULLTOKEN = '<<NULL>>')",
                _ => string.Empty
            };
            await using var create = context.CreateSqlContainer(
                $"CREATE {kind}TABLE {IntegrationObjectNameHelper.Table(context, TableName)} (" +
                $"{context.WrapObjectName("id")} {IntegrationObjectNameHelper.IntType(provider)} NOT NULL PRIMARY KEY, " +
                $"{context.WrapObjectName("v")} {entry.Declaration}{nullable}){suffix}");
            await create.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            return "declare: " + ex.GetType().Name + ": " + ex.Message.Replace('\n', ' ');
        }

        var gateway = new TableGateway<TRow, int>(context);
        var row = new TRow();
        idProperty.SetValue(row, 1);
        valueProperty.SetValue(row, Probe);

        try
        {
            await gateway.CreateAsync(row, context);
            var loaded = await gateway.RetrieveOneAsync(1, context);
            if (loaded == null || (Guid)valueProperty.GetValue(loaded)! != Probe)
            {
                return $"gateway: wrote {Probe}, read {(loaded == null ? "no row" : valueProperty.GetValue(loaded))}";
            }

            var mapped = await ReadMappedAsync(context, "1");
            if (mapped != Probe)
            {
                return $"DataReaderMapper: wrote {Probe}, read {mapped}";
            }

            var raw = await ReadRawAsync(context, "1");
            var rawFailure = CheckRaw(provider, entry, raw);
            if (rawFailure != null)
            {
                return rawFailure;
            }

            // A native uuid column is not bytes to the application, but its bytes in the database's own binary form are the
            // layout other software sees (SQL Server's uniqueidentifier is mixed-endian, PostgreSQL's uuid is RFC 4122).
            var nativeBytes = NativeBytesExpression(provider, entry);
            if (nativeBytes != null)
            {
                var native = await ReadRawAsync(context, "1", nativeBytes);
                if (native is not byte[] nativeStored ||
                    !nativeStored.AsSpan().SequenceEqual(Probe.ToByteArray(!MixedEndian(provider))))
                {
                    return $"native bytes {(native is byte[] b2 ? Convert.ToHexString(b2) : native ?? "null")}, expected " +
                           Convert.ToHexString(Probe.ToByteArray(!MixedEndian(provider)));
                }
            }

            // Bytes laid down by other software in the database's own order read back as the same Guid.
            if (entry.DbType == DbType.Binary)
            {
                var foreign = Probe.ToByteArray(!MixedEndian(provider));
                await using var insert = context.CreateSqlContainer();
                var p = insert.AddParameterWithValue("b", DbType.Binary, foreign);
                insert.Query.Append("INSERT INTO ").Append(IntegrationObjectNameHelper.Table(context, TableName))
                    .Append(" (").Append(context.WrapObjectName("id")).Append(", ").Append(context.WrapObjectName("v"))
                    .Append(") VALUES (2, ").Append(insert.MakeParameterName(p)).Append(')');
                await insert.ExecuteNonQueryAsync();

                var other = await gateway.RetrieveOneAsync(2, context);
                if (other == null || (Guid)valueProperty.GetValue(other)! != Probe)
                {
                    return $"foreign bytes {Convert.ToHexString(foreign)}: gateway read " +
                           (other == null ? "no row" : valueProperty.GetValue(other)!.ToString());
                }

                var otherMapped = await ReadMappedAsync(context, "2");
                if (otherMapped != Probe)
                {
                    return $"foreign bytes {Convert.ToHexString(foreign)}: DataReaderMapper read {otherMapped}";
                }
            }
        }
        catch (Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message.Replace('\n', ' ');
        }

        return null;
    }

    private static string? CheckRaw(SupportedDatabase provider, ColumnTypeDescriptor entry, object? raw)
    {
        switch (raw)
        {
            case byte[] bytes:
                var expected = Probe.ToByteArray(!MixedEndian(provider));
                return bytes.AsSpan().SequenceEqual(expected)
                    ? null
                    : $"stored bytes {Convert.ToHexString(bytes)}, expected {Convert.ToHexString(expected)} " +
                      $"({(MixedEndian(provider) ? "mixed-endian" : "RFC 4122")})";
            case Guid guid:
                return guid == Probe ? null : $"stored uuid {guid}, expected {Probe}";
            case string text:
                return string.Equals(text.Trim(), Probe.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? null
                    : $"stored text '{text}', expected {Probe}";
            default:
                return $"stored value {raw ?? "null"} ({raw?.GetType().Name}) is not bytes, a uuid or text";
        }
    }

    // SQL Server, Oracle and Sybase ASE drivers write a Guid in .NET's ToByteArray() order; every other
    // database's 16-byte binary holds the RFC 4122 order.
    private static bool MixedEndian(SupportedDatabase provider) =>
        provider is SupportedDatabase.SqlServer or SupportedDatabase.Oracle or SupportedDatabase.SybaseASE;

    private static async Task<Guid> ReadMappedAsync(IDatabaseContext context, string id)
    {
        await using var select = context.CreateSqlContainer();
        select.Query.Append("SELECT ").Append(context.WrapObjectName("v")).Append(" AS ")
            .Append(context.WrapObjectName("V")).Append(" FROM ")
            .Append(IntegrationObjectNameHelper.Table(context, TableName))
            .Append(" WHERE ").Append(context.WrapObjectName("id")).Append(" = ").Append(id);
        await using var reader = await select.ExecuteReaderAsync();
        var mapped = await DataReaderMapper.LoadObjectsFromDataReaderAsync<MappedGuid>(reader);
        return mapped.Count == 1 ? mapped[0].V : Guid.Empty;
    }

    // The expression that returns a native uuid column's own bytes, or null where the column isn't a native uuid we can ask.
    private static Func<string, string>? NativeBytesExpression(SupportedDatabase provider, ColumnTypeDescriptor entry) =>
        entry.DbType == DbType.Guid
            ? provider switch
            {
                SupportedDatabase.SqlServer when entry.Declaration == "UNIQUEIDENTIFIER" => column => $"CAST({column} AS BINARY(16))",
                SupportedDatabase.PostgreSql when entry.Declaration == "UUID" => column => $"uuid_send({column})",
                _ => null
            }
            : null;

    private static async Task<object?> ReadRawAsync(IDatabaseContext context, string id, Func<string, string>? project = null)
    {
        await using var select = context.CreateSqlContainer();
        select.Query.Append("SELECT ").Append(project == null
                ? context.WrapObjectName("v")
                : project(context.WrapObjectName("v"))).Append(" FROM ")
            .Append(IntegrationObjectNameHelper.Table(context, TableName))
            .Append(" WHERE ").Append(context.WrapObjectName("id")).Append(" = ").Append(id);
        await using var reader = await select.ExecuteReaderAsync();
        return await reader.ReadAsync() && !reader.IsDBNull(0) ? reader.GetValue(0) : null;
    }

    private sealed class MappedGuid
    {
        public Guid V { get; set; }
    }
}
