using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Npgsql;
using testbed.PostgreSQL;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// A76: PostGIS's raster type (the postgis_raster extension), the "spatial raster / other extensions" concept. A raster has no CLR
/// shape, so it is built, stored and measured in SQL: size, band count and a pixel value come back from the stored column.
/// Uses its own PostGIS container, like PostGisRoundTripTests.
/// </summary>
[Collection("IntegrationTests")]
public sealed class PostGisRasterTests : DatabaseTestBase
{
    public PostGisRasterTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    [SkippableFact]
    public async Task Raster_StoresBandsAndPixels()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async _ =>
        {
            var container = new PostgreSqlTestContainer("postgis/postgis:17-3.5");
            await container.StartAsync();
            try
            {
                await using (var setup = new DatabaseContext(container.ConnectionString, NpgsqlFactory.Instance))
                {
                    await using var ext = setup.CreateSqlContainer("CREATE EXTENSION IF NOT EXISTS postgis; CREATE EXTENSION IF NOT EXISTS postgis_raster");
                    await ext.ExecuteNonQueryAsync();
                    await using var table = setup.CreateSqlContainer("CREATE TABLE raster_doc (id INTEGER PRIMARY KEY, r raster)");
                    await table.ExecuteNonQueryAsync();
                    await using var insert = setup.CreateSqlContainer(
                        "INSERT INTO raster_doc VALUES (1, ST_SetValue(ST_AddBand(ST_MakeEmptyRaster(4, 3, 0, 0, 1, -1, 0, 0, 4326), '8BUI'::text, 0, NULL), 1, 2, 2, 200))");
                    await insert.ExecuteNonQueryAsync();
                }

                await using var context = new DatabaseContext(container.ConnectionString, NpgsqlFactory.Instance);
                async Task<int> Int(string sql)
                {
                    await using var sc = context.CreateSqlContainer(sql);
                    return await sc.ExecuteScalarRequiredAsync<int>();
                }

                Assert.Equal(4, await Int("SELECT ST_Width(r) FROM raster_doc WHERE id = 1"));
                Assert.Equal(3, await Int("SELECT ST_Height(r) FROM raster_doc WHERE id = 1"));
                Assert.Equal(1, await Int("SELECT ST_NumBands(r) FROM raster_doc WHERE id = 1"));
                Assert.Equal(4326, await Int("SELECT ST_SRID(r) FROM raster_doc WHERE id = 1"));
                Assert.Equal(200, await Int("SELECT CAST(ST_Value(r, 1, 2, 2) AS integer) FROM raster_doc WHERE id = 1"));
            }
            finally
            {
                await container.DisposeAsync();
            }
        });
    }
}
