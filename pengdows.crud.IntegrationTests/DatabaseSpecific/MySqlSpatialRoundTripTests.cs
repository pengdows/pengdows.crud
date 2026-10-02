using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Exercises <c>Geometry</c>/<c>Geography</c> through the actual CRUD mapper against a real
/// MySQL server. MySQL has no distinct native geography type — per
/// <c>GeographyConverter</c>'s own documented behavior, geodetic values are stored in an
/// ordinary <c>GEOMETRY</c> column with SRID 4326 and application-level geodetic semantics.
/// This is deliberately separate from unit tests that call the coercion registry directly: a
/// successful test proves parameter binding, MySQL's WKB wire format, and data-reader hydration
/// all agree on a real server — fakeDb cannot prove any of that.
/// </summary>
[Collection("IntegrationTests")]
public sealed class MySqlSpatialRoundTripTests : DatabaseTestBase
{
    public MySqlSpatialRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        new[] { SupportedDatabase.MySql, SupportedDatabase.MariaDb };

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context, "spatial_roundtrip");
        await using var table = context.CreateSqlContainer($"""
            CREATE TABLE IF NOT EXISTS {IntegrationObjectNameHelper.Table(context, "spatial_roundtrip")} (
                id              INTEGER PRIMARY KEY,
                geometry_value  GEOMETRY NOT NULL,
                geography_value GEOMETRY NOT NULL
            )
            """);
        await table.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// TYPE-018: Geometry/Geography are written in MySQL's internal format (a 4-byte
    /// little-endian SRID followed by WKB, encoded from WKT when the value only has text) and read
    /// back from it. Before the fix MySqlConnector refused the value object outright and MySql.Data
    /// sent raw WKT bytes, which the server rejects ("Cannot get geometry object from data you
    /// send to the GEOMETRY field").
    /// </summary>
    [SkippableFact]
    public async Task MySqlSpatialTypes_RoundTripThroughCrudMapper()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.MySql, SupportedDatabase.MariaDb }, async (provider, context) =>
        {
            var geographyWkb = BuildPointWkb(-87.6298, 41.8781);
            var entity = new MySqlSpatialEntity
            {
                Id = 1,
                GeometryValue = Geometry.FromWellKnownText("POLYGON((0 0, 4 0, 4 4, 0 0))", 0),
                GeographyValue = Geography.FromWellKnownBinary(geographyWkb, 4326)
            };

            var gateway = new TableGateway<MySqlSpatialEntity, int>(context);
            Assert.True(await gateway.CreateAsync(entity, context));

            await using (var check = context.CreateSqlContainer(
                             "SELECT ST_AsText(geometry_value), ST_SRID(geometry_value), ST_SRID(geography_value) " +
                             $"FROM {IntegrationObjectNameHelper.Table(context, "spatial_roundtrip")} WHERE id = 1"))
            await using (var reader = await check.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal("POLYGON((0 0,4 0,4 4,0 0))", reader.GetString(0));
                Assert.Equal(0L, Convert.ToInt64(reader.GetValue(1)));
                Assert.Equal(4326L, Convert.ToInt64(reader.GetValue(2)));
            }

            var loaded = await gateway.RetrieveOneAsync(1, context);
            Assert.NotNull(loaded);
            Assert.Equal(0, loaded!.GeometryValue.Srid);
            Assert.Equal(4326, loaded.GeographyValue.Srid);
            Assert.Equal(geographyWkb, loaded.GeographyValue.WellKnownBinary.ToArray());
            Output.WriteLine($"{provider}: Geometry (WKT polygon) and Geography (WKB point, SRID 4326) round-tripped");
        });
    }

    private static byte[] BuildPointWkb(double x, double y)
    {
        var wkb = new byte[21];
        wkb[0] = 1;
        BitConverter.GetBytes(1u).CopyTo(wkb, 1);
        BitConverter.GetBytes(x).CopyTo(wkb, 5);
        BitConverter.GetBytes(y).CopyTo(wkb, 13);
        return wkb;
    }
}

[Table("spatial_roundtrip")]
internal sealed class MySqlSpatialEntity
{
    [Id][Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("geometry_value", DbType.Object)] public Geometry GeometryValue { get; set; } = null!;
    [Column("geography_value", DbType.Object)] public Geography GeographyValue { get; set; } = null!;
}
