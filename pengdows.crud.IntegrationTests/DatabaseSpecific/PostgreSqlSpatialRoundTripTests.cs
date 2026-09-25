using System.Data;
using pengdows.crud.@internal;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

[Collection("IntegrationTests")]
public sealed class PostgreSqlSpatialRoundTripTests : DatabaseTestBase
{
    private static long _nextId;

    public PostgreSqlSpatialRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        new[] { SupportedDatabase.PostgreSql };

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.RegisterEntity<PostgreSqlSpatialEntity>();
        await DropTableIfExistsAsync(context, "postgresql_spatial_roundtrip");

        await using var container = context.CreateSqlContainer($"""
            CREATE TABLE {IntegrationObjectNameHelper.Table(context, "postgresql_spatial_roundtrip")} (
                {context.WrapObjectName("id")} BIGINT PRIMARY KEY,
                {context.WrapObjectName("location")} BYTEA NOT NULL
            )
            """);
        await container.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public Task WkbWriteUsesSridAwarePayloadThroughRealCrudPath()
    {
        return RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            var gateway = new TableGateway<PostgreSqlSpatialEntity, long>(context);
            var entity = new PostgreSqlSpatialEntity
            {
                Id = Interlocked.Increment(ref _nextId),
                Location = Geometry.FromWellKnownBinary(
                    new byte[]
                    {
                        1, 1, 0, 0, 0,
                        0, 0, 0, 0, 0, 0, 0, 240, 63,
                        0, 0, 0, 0, 0, 0, 0, 64
                    },
                    4326)
            };

            Assert.True(await gateway.CreateAsync(entity, context));

            var retrieved = await gateway.RetrieveOneAsync(entity.Id, context);

            Assert.NotNull(retrieved);
            Assert.Equal(4326, retrieved!.Location.Srid);
            Assert.Equal(SpatialFormat.WellKnownBinary, retrieved.Location.Format);
        });
    }
}

[Table("postgresql_spatial_roundtrip")]
internal sealed class PostgreSqlSpatialEntity
{
    [Id]
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [Column("location", DbType.Object)]
    public Geometry Location { get; set; } = null!;
}
