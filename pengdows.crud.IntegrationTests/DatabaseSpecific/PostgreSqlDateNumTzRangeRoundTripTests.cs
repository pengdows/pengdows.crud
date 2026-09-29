using System.Data;
using pengdows.crud.@internal;
using pengdows.crud.infrastructure;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// TYPE-009: daterange (<c>Range&lt;DateOnly&gt;</c>), numrange (<c>Range&lt;decimal&gt;</c>) and
/// tstzrange (<c>Range&lt;DateTimeOffset&gt;</c>) written through a gateway into real range columns and
/// read back, with the stored bounds checked on the server, so no application code is needed for any
/// built-in PostgreSQL range type.
/// </summary>
[Collection("IntegrationTests")]
public class PostgreSqlDateNumTzRangeRoundTripTests : DatabaseTestBase
{
    private static long _nextId;

    // CockroachDB has no range types.
    private static readonly SupportedDatabase[] RangeProviders =
    {
        SupportedDatabase.PostgreSql,
        SupportedDatabase.YugabyteDb
    };

    public PostgreSqlDateNumTzRangeRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return base.GetSupportedProviders().Where(RangeProviders.Contains).ToArray();
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.RegisterEntity<RangeTypesEntity>();
        await DropTableIfExistsAsync(context, "range_types_entity");

        var table = IntegrationObjectNameHelper.Table(context, "range_types_entity");
        await using var container = context.CreateSqlContainer($@"
CREATE TABLE {table} (
    {context.WrapObjectName("id")} BIGINT PRIMARY KEY,
    {context.WrapObjectName("days")} DATERANGE NOT NULL,
    {context.WrapObjectName("amounts")} NUMRANGE NOT NULL,
    {context.WrapObjectName("window")} TSTZRANGE NOT NULL
)");
        await container.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public Task DateNumAndTstzRanges_RoundTripAndStoreTheirMeaning()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<RangeTypesEntity, long>(context);
            var entity = new RangeTypesEntity
            {
                Id = Interlocked.Increment(ref _nextId),
                Days = new Range<DateOnly>(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), true, false),
                Amounts = new Range<decimal>(1.5m, 9.25m, true, true),
                // A non-UTC offset: Npgsql only writes offset 0, so the library must normalize.
                Window = new Range<DateTimeOffset>(
                    new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(-5)),
                    null, true, false)
            };

            Assert.True(await gateway.CreateAsync(entity, context));

            var retrieved = await gateway.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
            Assert.Equal(entity.Days, retrieved!.Days);
            Assert.Equal(entity.Amounts, retrieved.Amounts);
            Assert.Equal(entity.Window.Lower!.Value.UtcDateTime, retrieved.Window.Lower!.Value.UtcDateTime);
            Assert.False(retrieved.Window.HasUpperBound);

            var table = IntegrationObjectNameHelper.Table(context, "range_types_entity");
            await using var sc = context.CreateSqlContainer(
                $"SELECT CAST(lower({context.WrapObjectName("days")}) AS TEXT) || '|' || " +
                $"CAST(upper({context.WrapObjectName("amounts")}) AS TEXT) || '|' || " +
                $"CAST(extract(epoch FROM lower({context.WrapObjectName("window")})) AS BIGINT) " +
                $"FROM {table} WHERE {context.WrapObjectName("id")} = ");
            var p = sc.AddParameterWithValue("id", DbType.Int64, entity.Id);
            sc.Query.Append(sc.MakeParameterName(p));
            var stored = await sc.ExecuteScalarRequiredAsync<string>();

            var expectedEpoch = entity.Window.Lower!.Value.ToUnixTimeSeconds();
            Assert.Equal($"2026-09-01|9.25|{expectedEpoch}", stored);
        });
    }

    [Table("range_types_entity")]
    public class RangeTypesEntity
    {
        [Id(true)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("days", DbType.Object)]
        public Range<DateOnly> Days { get; set; }

        [Column("amounts", DbType.Object)]
        public Range<decimal> Amounts { get; set; }

        [Column("window", DbType.Object)]
        public Range<DateTimeOffset> Window { get; set; }
    }
}
