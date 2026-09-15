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
        new[] { SupportedDatabase.MySql };

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
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
    /// CONFIRMED LIVE BUG, tracked here rather than left silently unimplemented (deliberately not
    /// fixed in this pass — see reasoning below):
    /// <c>SpatialConverter.CreateMySqlSpatial</c> (in <c>pengdows.crud/types/coercion/</c> — no,
    /// actually <c>pengdows.crud/types/converters/SpatialConverter.cs</c>) has two independent
    /// write-side defects for MySQL, both reproduced against a real MySQL 8 container:
    /// <list type="number">
    /// <item>The WKT branch UTF8-encodes the literal well-known-text string (e.g. the ASCII bytes
    /// of <c>"POINT(10 20)"</c>) and sends that directly as the GEOMETRY column's binary value —
    /// MySQL does not accept raw WKT text as column bytes without an <c>ST_GeomFromText(...)</c>
    /// SQL-function wrapper, so this is not "slightly wrong," it sends fundamentally the wrong
    /// payload shape.</item>
    /// <item>The WKB branch sends <c>value.WellKnownBinary</c> unmodified — real, valid WKB — but
    /// MySQL's actual column storage format prepends a mandatory 4-byte little-endian SRID before
    /// the WKB body ("MySQL internal geometry format"); without that prefix the server rejects it
    /// identically.</item>
    /// </list>
    /// Both fail with the same live-confirmed server error: <c>"Cannot get geometry object from
    /// data you send to the GEOMETRY field"</c>. A correct fix needs a real WKT→WKB encoder (for
    /// every WKT shape <c>Geometry</c>/<c>Geography</c> document supporting — Point, LineString,
    /// Polygon, and the Multi*/Collection variants) AND a matching MySQL-specific 4-byte-SRID
    /// prepend-on-write / strip-on-read pair (the read side has the mirror-image gap: MySQL
    /// returns that same SRID-prefixed format from <c>GetValue()</c>, which
    /// <c>SpatialConverter.TryConvertFromProvider</c>'s generic <c>byte[]</c> branch would also
    /// misparse as if it were pure WKB). That is a real, feature-sized fix — out of scope for an
    /// integration-test-coverage pass — so this test proves and locks down the CURRENT (broken)
    /// behavior rather than silently omitting MySQL spatial coverage entirely.
    /// </summary>
    [SkippableFact]
    public async Task MySqlSpatialTypes_WriteThroughCrudMapper_CurrentlyFailsServerSide()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.MySql, async context =>
        {
            var entity = new MySqlSpatialEntity
            {
                Id = 1,
                GeometryValue = Geometry.FromWellKnownText("POINT(10 20)", 0),
                GeographyValue = Geography.FromWellKnownText("POINT(-87.6298 41.8781)", 4326)
            };

            var gateway = new TableGateway<MySqlSpatialEntity, int>(context);

            var ex = await Assert.ThrowsAsync<DatabaseOperationException>(
                () => gateway.CreateAsync(entity, context).AsTask());

            Assert.Contains("geometry", ex.Message, StringComparison.OrdinalIgnoreCase);

            Output.WriteLine(
                "MySql: confirmed spatial write still fails server-side (tracked bug, not yet " +
                "fixed) — " + ex.Message);
        });
    }
}

[Table("spatial_roundtrip")]
internal sealed class MySqlSpatialEntity
{
    [Id][Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("geometry_value", DbType.Object)] public Geometry GeometryValue { get; set; } = null!;
    [Column("geography_value", DbType.Object)] public Geography GeographyValue { get; set; } = null!;
}
