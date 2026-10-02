using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// TYPE-002: SQL Server geometry/geography through every gateway write path without
/// Microsoft.SqlServer.Types. The gateways bind a big-endian SRID + WKB and render
/// geometry::STGeomFromWKB(...) / geography::STGeomFromWKB(...), so the server builds the
/// instance: the SRID is kept and validity is the server's own verdict (an invalid polygon stays
/// invalid rather than being trusted as valid). Reads decode SQL Server's stored encoding.
/// </summary>
[Collection("IntegrationTests")]
public sealed class SqlServerSpatialRoundTripTests : DatabaseTestBase
{
    private const string Table = "mssql_spatial";

    public SqlServerSpatialRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() => new[] { SupportedDatabase.SqlServer };

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context, Table);
        await using var create = context.CreateSqlContainer(
            $"CREATE TABLE {IntegrationObjectNameHelper.Table(context, Table)} " +
            "(id INT NOT NULL PRIMARY KEY, geom GEOMETRY NULL, geog GEOGRAPHY NULL)");
        await create.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task SpatialValues_RoundTripThroughEveryWritePath()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SqlServer }, async (_, context) =>
        {
            var gateway = new TableGateway<SpatialRow, int>(context);

            // Single-row create: WKT geometry with a non-default SRID, WKB geography.
            Assert.True(await gateway.CreateAsync(new SpatialRow
            {
                Id = 1,
                Geom = Geometry.FromWellKnownText("POLYGON((0 0, 4 0, 4 4, 0 0))", 3857),
                Geog = Geography.FromWellKnownText("POINT(-87.6298 41.8781)", 4269)
            }, context));
            await AssertStoredAsync(context, 1, "POLYGON ((0 0, 4 0, 4 4, 0 0))", 3857, true, 8.0, 4269);

            // An invalid polygon (a bowtie) is stored and judged invalid by the server.
            Assert.True(await gateway.CreateAsync(new SpatialRow
            {
                Id = 2,
                Geom = Geometry.FromWellKnownText("POLYGON((0 0, 1 1, 1 0, 0 1, 0 0))", 0)
            }, context));
            await AssertStoredAsync(context, 2, "POLYGON ((0 0, 1 1, 1 0, 0 1, 0 0))", 0, false, null, null);

            // Update.
            var row1 = (await gateway.RetrieveOneAsync(1, context))!;
            Assert.Equal(3857, row1.Geom!.Srid);
            Assert.Equal(4269, row1.Geog!.Srid);
            row1.Geom = Geometry.FromWellKnownText("LINESTRING(0 0, 10 10)", 27700);
            Assert.Equal(1, await gateway.UpdateAsync(row1, context));
            await AssertStoredAsync(context, 1, "LINESTRING (0 0, 10 10)", 27700, true, 0.0, 4269);

            // Upsert (MERGE), inserting and then updating.
            var upserted = new SpatialRow { Id = 3, Geom = Geometry.FromWellKnownText("POINT(1 2)", 3857) };
            Assert.True(await gateway.UpsertAsync(upserted, context) > 0);
            upserted.Geom = Geometry.FromWellKnownText("POINT(3 4)", 3857);
            Assert.True(await gateway.UpsertAsync(upserted, context) > 0);
            await AssertStoredAsync(context, 3, "POINT (3 4)", 3857, true, 0.0, null);

            // Batch create and batch update, with a NULL inlined among the values.
            await gateway.BatchCreateAsync(new[]
            {
                new SpatialRow { Id = 4, Geom = Geometry.FromWellKnownText("MULTIPOINT((1 2), (3 4))", 3857) },
                new SpatialRow { Id = 5, Geog = Geography.FromWellKnownText("LINESTRING(-122.3 47.6, -122.2 47.7)", 4326) }
            }, context);
            await AssertStoredAsync(context, 4, "MULTIPOINT ((1 2), (3 4))", 3857, true, 0.0, null);
            var five = (await gateway.RetrieveOneAsync(5, context))!;
            Assert.Null(five.Geom);
            Assert.Equal(4326, five.Geog!.Srid);

            five.Geom = Geometry.FromWellKnownText("GEOMETRYCOLLECTION(POINT(1 1), LINESTRING(0 0, 2 2))", 3857);
            await gateway.BatchUpdateAsync(new[] { five }, context);
            await AssertStoredAsync(context, 5, "GEOMETRYCOLLECTION (POINT (1 1), LINESTRING (0 0, 2 2))", 3857, true, 0.0, 4326);

            // Reads decode to the same shapes (WKB compared through the server's own text form).
            foreach (var row in await gateway.RetrieveAsync(new[] { 1, 2, 3, 4, 5 }, context))
            {
                Assert.NotNull(row.Geom);
            }

            var reread = (await gateway.RetrieveOneAsync(1, context))!;
            Assert.Equal(27700, reread.Geom!.Srid);
            Assert.Equal(-87.6298, BitConverter.ToDouble(reread.Geog!.WellKnownBinary.Span[5..13]), 10);
            Assert.Equal(41.8781, BitConverter.ToDouble(reread.Geog.WellKnownBinary.Span[13..21]), 10);
            Output.WriteLine("SqlServer: geometry/geography round-tripped through create, update, upsert and batch paths");
        });
    }

    private static async Task AssertStoredAsync(IDatabaseContext context, int id, string text, int srid, bool valid,
        double? area, int? geographySrid)
    {
        await using var check = context.CreateSqlContainer(
            "SELECT geom.STAsText(), geom.STSrid, geom.STIsValid(), " +
            "CASE WHEN geom.STIsValid() = 1 THEN geom.STArea() END, geog.STSrid " +
            $"FROM {IntegrationObjectNameHelper.Table(context, Table)} WHERE id = {id}");
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(text, reader.GetString(0));
        Assert.Equal(srid, reader.GetInt32(1));
        Assert.Equal(valid, reader.GetBoolean(2));
        if (area != null)
        {
            Assert.Equal(area.Value, reader.GetDouble(3), 6);
        }

        if (geographySrid == null)
        {
            Assert.True(reader.IsDBNull(4));
        }
        else
        {
            Assert.Equal(geographySrid.Value, reader.GetInt32(4));
        }
    }

    [Table(Table)]
    internal sealed class SpatialRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("geom", DbType.Object)] public Geometry? Geom { get; set; }
        [Column("geog", DbType.Object)] public Geography? Geog { get; set; }
    }
}
