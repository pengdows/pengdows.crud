using System.Data;
using Npgsql;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using testbed.PostgreSQL;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// TYPE-002: Geometry/Geography against real PostGIS geometry/geography columns (PostGIS 3.5).
/// Writes bind EWKB (WKT is encoded to WKB; text was refused as bytea); reads decode the EWKB that
/// Npgsql returns through GetBytes, since without NetTopologySuite it has no handler for either
/// type. Before, WKT values could not be written and neither column could be read.
/// </summary>
[Collection("IntegrationTests")]
public sealed class PostGisRoundTripTests : DatabaseTestBase
{
    public PostGisRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    [SkippableFact]
    public async Task GeometryAndGeography_RoundTripThroughPostGisColumns()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async _ =>
        {
            var container = new PostgreSqlTestContainer("postgis/postgis:17-3.5");
            await container.StartAsync();
            try
            {
                await using (var setup = new DatabaseContext(container.ConnectionString, NpgsqlFactory.Instance))
                await using (var ddl = setup.CreateSqlContainer(
                                 "CREATE EXTENSION IF NOT EXISTS postgis; " +
                                 "CREATE TABLE \"postgis_rows\" (\"id\" INTEGER NOT NULL PRIMARY KEY, " +
                                 "\"geom\" geometry NULL, \"geog\" geography NULL)"))
                {
                    await ddl.ExecuteNonQueryAsync();
                }

                // A context created after the extension, so Npgsql's type catalog has PostGIS's types.
                await using var context = new DatabaseContext(container.ConnectionString, NpgsqlFactory.Instance);
                var gateway = new TableGateway<PostGisRow, int>(context);
                var rows = new[]
                {
                    new PostGisRow
                    {
                        Id = 1,
                        Geom = Geometry.FromWellKnownText("POLYGON((0 0, 4 0, 4 4, 0 0))", 3857),
                        Geog = Geography.FromWellKnownText("POINT(-87.6298 41.8781)", 4326)
                    },
                    new PostGisRow
                    {
                        Id = 2,
                        Geom = Geometry.FromWellKnownBinary(WellKnownTextEncoder.Encode("LINESTRING(0 0, 1 1)"), 27700),
                        Geog = null
                    },
                };
                foreach (var row in rows)
                {
                    Assert.True(await gateway.CreateAsync(row, context));
                }

                await using (var check = context.CreateSqlContainer(
                                 "SELECT ST_AsText(\"geom\"), ST_SRID(\"geom\"), ST_AsText(\"geog\"), ST_SRID(\"geog\") " +
                                 "FROM \"postgis_rows\" WHERE \"id\" = 1"))
                await using (var reader = await check.ExecuteReaderAsync())
                {
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal("POLYGON((0 0,4 0,4 4,0 0))", reader.GetString(0));
                    Assert.Equal(3857, reader.GetInt32(1));
                    Assert.Equal("POINT(-87.6298 41.8781)", reader.GetString(2));
                    Assert.Equal(4326, reader.GetInt32(3));
                }

                var first = (await gateway.RetrieveOneAsync(1, context))!;
                Assert.Equal(3857, first.Geom!.Srid);
                Assert.Equal(WellKnownTextEncoder.Encode("POLYGON((0 0, 4 0, 4 4, 0 0))"), first.Geom.WellKnownBinary.ToArray());
                Assert.Equal(4326, first.Geog!.Srid);
                Assert.Equal(WellKnownTextEncoder.Encode("POINT(-87.6298 41.8781)"), first.Geog.WellKnownBinary.ToArray());

                var second = (await gateway.RetrieveOneAsync(2, context))!;
                Assert.Equal(27700, second.Geom!.Srid);
                Assert.Null(second.Geog);

                // Update writes through the same path.
                second.Geom = Geometry.FromWellKnownText("POINT(5 6)", 27700);
                Assert.Equal(1, await gateway.UpdateAsync(second, context));
                Assert.Equal(WellKnownTextEncoder.Encode("POINT(5 6)"),
                    (await gateway.RetrieveOneAsync(2, context))!.Geom!.WellKnownBinary.ToArray());

                await using (var plain = context.CreateSqlContainer(
                                 "SELECT \"id\" AS \"Id\", \"geom\" AS \"Geom\", \"geog\" AS \"Geog\" FROM \"postgis_rows\" WHERE \"id\" = 1"))
                await using (var reader = await plain.ExecuteReaderAsync())
                {
                    var mapped = Assert.Single(await DataReaderMapper.LoadAsync<PostGisRow>(reader, MapperOptions.Default));
                    Assert.Equal(3857, mapped.Geom!.Srid);
                    Assert.Equal(4326, mapped.Geog!.Srid);
                }

                Output.WriteLine("PostGIS: geometry/geography written as EWKB and read back through the gateway and DataReaderMapper");
            }
            finally
            {
                await container.DisposeAsync();
            }
        });
    }

    [Table("postgis_rows")]
    internal sealed class PostGisRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("geom", DbType.Object)] public Geometry? Geom { get; set; }
        [Column("geog", DbType.Object)] public Geography? Geog { get; set; }
    }
}
