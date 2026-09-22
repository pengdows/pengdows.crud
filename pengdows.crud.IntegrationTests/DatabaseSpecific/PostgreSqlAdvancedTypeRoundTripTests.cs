using System.Data;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using pengdows.crud.attributes;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit.Abstractions;
using JsonValue = pengdows.crud.types.valueobjects.JsonValue;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Exercises the advanced CLR types through the actual CRUD mapper and Npgsql.
/// This is deliberately separate from unit tests that call the coercion registry
/// directly: a successful test proves both parameter binding and data-reader
/// hydration work without provider-specific application code.
/// </summary>
[Collection("IntegrationTests")]
public sealed class PostgreSqlAdvancedTypeRoundTripTests : DatabaseTestBase
{
    public PostgreSqlAdvancedTypeRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        new[] { SupportedDatabase.PostgreSql };

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await using var extension = context.CreateSqlContainer("CREATE EXTENSION IF NOT EXISTS hstore");
        await extension.ExecuteNonQueryAsync();

        // Npgsql caches a connection string's Postgres type catalog (OIDs for every type it knows
        // about, including extension/plugin types like hstore) the first time ANY connection for
        // that exact connection string opens — and pengdows.crud's own DatabaseContext already
        // opened at least one connection during product/version detection before this method ever
        // ran, i.e. before the CREATE EXTENSION above existed. Confirmed live with a minimal repro
        // (open a connection, THEN create the extension, THEN try to bind an HStore value on the
        // same connection string): it fails with "The NpgsqlDbType 'Hstore' isn't present in your
        // database" even though the extension is genuinely installed — NOT because pengdows.crud's
        // NpgsqlDbType.Hstore binding (ProviderParameterFactory.cs) is wrong, and NOT because of a
        // missing NpgsqlDataSourceBuilder.UseHstore() opt-in (that method doesn't even exist in
        // Npgsql 9 — Hstore needs no such opt-in once the type catalog is fresh). The fix is
        // NpgsqlConnection.ReloadTypesAsync(), an instance method requiring an already-open,
        // credentialed connection. Building an independent connection from a copied-out connection
        // string doesn't work either — Npgsql's own NpgsqlConnection.ConnectionString getter omits
        // the password on readback (standard ADO.NET provider behavior, confirmed live: "No
        // password has been provided"), on top of IDatabaseContext.ConnectionString being
        // deliberately redacted. So this borrows a connection through pengdows.crud's own
        // (internal, InternalsVisibleTo-friend) connection-acquisition path and calls
        // ReloadTypesAsync directly on the real, already-authenticated Npgsql connection object —
        // safe to do mid-pool-lifecycle since it only refreshes cached type metadata and touches no
        // transaction/data state — then returns the connection through the normal API immediately
        // after, exactly like ConnectionManagement tests already borrow/return connections.
        var borrowed = context.GetConnection(ExecutionType.Write);
        try
        {
            if (borrowed.State != ConnectionState.Open)
            {
                await borrowed.OpenAsync();
            }

            var underlying = ((IInternalConnectionWrapper)borrowed).UnderlyingConnection;
            var npgsqlConnection = (NpgsqlConnection)underlying;
            await npgsqlConnection.ReloadTypesAsync();

            // ReloadTypesAsync's own doc comment is explicit: it "reloads the types for this
            // connection only. Type changes will appear for other connections only after they are
            // re-opened from the pool." Under a small/idle test run the pool is small enough that
            // this borrowed connection is very likely the one later reused for the actual
            // RetrieveOneAsync call below, masking the gap — but under full-suite load (a larger
            // pool with more connections already open from before this method ran, e.g.
            // DatabaseContext's own product/version-detection connection at construction) a
            // DIFFERENT, never-reloaded connection can serve that call instead, still carrying the
            // stale pre-extension type catalog: confirmed live as an intermittent
            // InvalidCastException ("Reading as 'System.Object' is not supported for fields having
            // DataTypeName '-'") that this borrowed-connection-only reload doesn't fully prevent.
            // ClearPool forces every OTHER idle/busy connection for this connection string to be
            // discarded and reopened fresh on next use, so whichever connection actually serves
            // the later reads is guaranteed to pick up the reloaded type catalog too.
            NpgsqlConnection.ClearPool(npgsqlConnection);
        }
        finally
        {
            context.CloseAndDisposeConnection(borrowed);
        }

        await using var table = context.CreateSqlContainer($"""
            CREATE TABLE IF NOT EXISTS {IntegrationObjectNameHelper.Table(context, "advanced_type_roundtrip")} (
                id              INTEGER PRIMARY KEY,
                json_value      JSONB NOT NULL,
                json_document   JSONB NOT NULL,
                json_element    JSONB NOT NULL,
                int_array       INTEGER[] NOT NULL,
                text_array      TEXT[] NOT NULL,
                int_range       INT4RANGE NOT NULL,
                date_range      TSRANGE NOT NULL,
                long_range      INT8RANGE NOT NULL,
                inet_value      INET NOT NULL,
                cidr_value      CIDR NOT NULL,
                mac_value       MACADDR NOT NULL,
                interval_value  INTERVAL NOT NULL,
                hstore_value    HSTORE NOT NULL
            )
            """);
        await table.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task PostgreSqlAdvancedTypes_RoundTripThroughCrudMapper()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            using var document = JsonDocument.Parse("{\"kind\":\"document\",\"count\":2}");
            var element = JsonDocument.Parse("{\"kind\":\"element\",\"enabled\":true}").RootElement.Clone();

            var expected = new PostgreSqlAdvancedTypeEntity
            {
                Id = 1,
                JsonValue = JsonValue.Parse("{\"kind\":\"value\",\"count\":1}"),
                JsonDocument = document,
                JsonElement = element,
                IntArray = [2, 4, 8],
                TextArray = ["alpha", "beta"],
                IntRange = new Range<int>(2, 10, true, false),
                DateRange = new Range<DateTime>(
                    new DateTime(2024, 1, 1), new DateTime(2024, 2, 1), true, false),
                LongRange = new Range<long>(100L, 200L, true, false),
                // Npgsql exposes inet as IPAddress on read and therefore does
                // not preserve an optional inet prefix; CIDR below covers the
                // prefix-bearing network case.
                InetValue = Inet.Parse("192.168.1.20"),
                CidrValue = Cidr.Parse("192.168.1.0/24"),
                MacValue = MacAddress.Parse("08:00:2B:01:02:03"),
                IntervalValue = new PostgreSqlInterval(3, 2, 4_000_000),
                HStoreValue = new HStore(new Dictionary<string, string?>
                {
                    ["role"] = "admin",
                    ["nickname"] = null,
                    ["needs quoting"] = "has, special=>chars"
                })
            };

            var gateway = new TableGateway<PostgreSqlAdvancedTypeEntity, int>(context);
            await gateway.CreateAsync(expected, context);

            await using (var raw = context.CreateSqlContainer(
                             "SELECT json_value::text FROM advanced_type_roundtrip WHERE id = 1"))
            await using (var rawReader = await raw.ExecuteReaderAsync())
            {
                Assert.True(await rawReader.ReadAsync());
                Assert.True(JsonNode.DeepEquals(
                    JsonNode.Parse(expected.JsonValue.AsString()),
                    JsonNode.Parse(rawReader.GetString(0))));
            }

            var actual = await gateway.RetrieveOneAsync(expected.Id, context);

            Assert.NotNull(actual);
            Assert.Equal(expected.Id, actual!.Id);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.JsonValue.AsString()),
                JsonNode.Parse(actual.JsonValue.AsString())));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.JsonDocument.RootElement.GetRawText()),
                JsonNode.Parse(actual.JsonDocument.RootElement.GetRawText())));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.JsonElement.GetRawText()),
                JsonNode.Parse(actual.JsonElement.GetRawText())));
            Assert.Equal(expected.IntArray, actual.IntArray);
            Assert.Equal(expected.TextArray, actual.TextArray);
            Assert.Equal(expected.IntRange, actual.IntRange);
            Assert.Equal(expected.DateRange, actual.DateRange);
            Assert.Equal(expected.LongRange, actual.LongRange);
            Assert.Equal(expected.InetValue, actual.InetValue);
            Assert.Equal(expected.CidrValue, actual.CidrValue);
            Assert.Equal(expected.MacValue, actual.MacValue);

            // PostgreSQL's ADO.NET interval provider exposes a TimeSpan. The
            // CLR value object intentionally preserves the day/time component;
            // months are not representable in TimeSpan and are therefore not
            // asserted as part of this provider round-trip.
            Assert.Equal(expected.IntervalValue.ToTimeSpan(), actual.IntervalValue.ToTimeSpan());

            Assert.Equal(expected.HStoreValue, actual.HStoreValue);
            Assert.Equal("admin", actual.HStoreValue["role"]);
            Assert.Null(actual.HStoreValue["nickname"]);
            Assert.Equal("has, special=>chars", actual.HStoreValue["needs quoting"]);
        });
    }

    [SkippableFact]
    public async Task MacAddress_EightByteAddress_RoundTripsThroughMacAddr8ColumnAndWherePredicate()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            await using var table = context.CreateSqlContainer($"""
                CREATE TABLE IF NOT EXISTS {IntegrationObjectNameHelper.Table(context, "macaddr8_roundtrip")} (
                    id          INTEGER PRIMARY KEY,
                    mac8_value  MACADDR8 NOT NULL
                )
                """);
            await table.ExecuteNonQueryAsync();

            var eightByte = MacAddress.Parse("08:00:2B:01:02:03:04:05");
            var expected = new Macaddr8Entity { Id = 1, Mac8Value = eightByte };

            var gateway = new TableGateway<Macaddr8Entity, int>(context);
            await gateway.CreateAsync(expected, context);

            var actual = await gateway.RetrieveOneAsync(expected.Id, context);
            Assert.NotNull(actual);
            Assert.Equal(expected.Mac8Value, actual!.Mac8Value);

            // Prove operator resolution works too, not just assignment/coercion on INSERT -
            // a mismatched NpgsqlDbType (e.g. macaddr instead of macaddr8) can fail here even
            // when a plain INSERT succeeds.
            await using var predicate = context.CreateSqlContainer(
                $"SELECT id FROM {IntegrationObjectNameHelper.Table(context, "macaddr8_roundtrip")} WHERE mac8_value = ");
            var p = predicate.AddParameterWithValue("mac8", DbType.Object, eightByte);
            predicate.Query.Append(predicate.MakeParameterName(p));
            var foundId = await predicate.ExecuteScalarOrNullAsync<int>();
            Assert.Equal(expected.Id, foundId);
        });
    }
}

[Table("macaddr8_roundtrip")]
internal sealed class Macaddr8Entity
{
    [Id][Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("mac8_value", DbType.Object)] public MacAddress Mac8Value { get; set; }
}

[Table("advanced_type_roundtrip")]
internal sealed class PostgreSqlAdvancedTypeEntity
{
    [Id][Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("json_value", DbType.Object)] public JsonValue JsonValue { get; set; }
    [Column("json_document", DbType.Object)] public JsonDocument JsonDocument { get; set; } = null!;
    [Column("json_element", DbType.Object)] public JsonElement JsonElement { get; set; }
    [Column("int_array", DbType.Object)] public int[] IntArray { get; set; } = [];
    [Column("text_array", DbType.Object)] public string[] TextArray { get; set; } = [];
    [Column("int_range", DbType.Object)] public Range<int> IntRange { get; set; }
    [Column("date_range", DbType.Object)] public Range<DateTime> DateRange { get; set; }
    [Column("long_range", DbType.Object)] public Range<long> LongRange { get; set; }
    [Column("inet_value", DbType.Object)] public Inet InetValue { get; set; }
    [Column("cidr_value", DbType.Object)] public Cidr CidrValue { get; set; }
    [Column("mac_value", DbType.Object)] public MacAddress MacValue { get; set; }
    [Column("interval_value", DbType.Object)] public PostgreSqlInterval IntervalValue { get; set; }
    [Column("hstore_value", DbType.Object)] public HStore HStoreValue { get; set; }
}
