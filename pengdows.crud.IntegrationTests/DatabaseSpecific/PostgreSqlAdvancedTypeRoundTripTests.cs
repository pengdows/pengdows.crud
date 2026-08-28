using System.Data;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using pengdows.crud.attributes;
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
                interval_value  INTERVAL NOT NULL
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
                IntervalValue = new PostgreSqlInterval(3, 2, 4_000_000)
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
        });
    }
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
}
