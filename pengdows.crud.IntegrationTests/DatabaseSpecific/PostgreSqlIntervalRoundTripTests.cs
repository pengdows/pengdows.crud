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
/// A <see cref="PostgreSqlInterval"/> written through a gateway must round-trip through a real
/// PostgreSQL-family <c>interval</c> column with months, days and microseconds intact.
/// </summary>
[Collection("IntegrationTests")]
public class PostgreSqlIntervalRoundTripTests : DatabaseTestBase
{
    private static long _nextId;

    private static readonly SupportedDatabase[] IntervalProviders =
    {
        SupportedDatabase.PostgreSql,
        SupportedDatabase.CockroachDb,
        SupportedDatabase.YugabyteDb
    };

    public PostgreSqlIntervalRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return base.GetSupportedProviders().Where(IntervalProviders.Contains).ToArray();
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.RegisterEntity<IntervalEntity>();
        await DropTableIfExistsAsync(context, "interval_entity");

        var table = IntegrationObjectNameHelper.Table(context, "interval_entity");
        await using var container = context.CreateSqlContainer($@"
CREATE TABLE {table} (
    {context.WrapObjectName("id")} BIGINT PRIMARY KEY,
    {context.WrapObjectName("duration")} INTERVAL NOT NULL
)");
        await container.ExecuteNonQueryAsync();
    }

    public static IEnumerable<object[]> Intervals() => new[]
    {
        // months, days, microseconds
        new object[] { 14, 3, 4L * 3_600_000_000 + 5 * 60_000_000 + 6_000_789 },
        new object[] { 0, 0, 36L * 3_600_000_000 },
        new object[] { 0, 10, 0L },
        new object[] { -2, -1, -90_000_000L }
    };

    [SkippableTheory]
    [MemberData(nameof(Intervals))]
    public Task Interval_RoundTrips(int months, int days, long microseconds)
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<IntervalEntity, long>(context);
            var expected = new PostgreSqlInterval(months, days, microseconds);
            var entity = new IntervalEntity { Id = Interlocked.Increment(ref _nextId), Duration = expected };

            Assert.True(await gateway.CreateAsync(entity, context));

            var retrieved = await gateway.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
            Assert.Equal(expected, retrieved!.Duration);
            Output.WriteLine($"{provider}: {expected} round-tripped");
        });
    }

    // Same round-trip through DataReaderMapper, which hydrates independently of the gateway.
    [SkippableTheory]
    [MemberData(nameof(Intervals))]
    public Task Interval_RoundTrips_ThroughDataReaderMapper(int months, int days, long microseconds)
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<IntervalEntity, long>(context);
            var expected = new PostgreSqlInterval(months, days, microseconds);
            var entity = new IntervalEntity { Id = Interlocked.Increment(ref _nextId), Duration = expected };
            Assert.True(await gateway.CreateAsync(entity, context));

            await using var sc = context.CreateSqlContainer();
            sc.Query.Append("SELECT ")
                .Append(sc.WrapObjectName("id")).Append(", ")
                .Append(sc.WrapObjectName("duration"))
                .Append(" FROM ").Append(IntegrationObjectNameHelper.Table(context, "interval_entity"))
                .Append(" WHERE ").Append(sc.WrapObjectName("id")).Append(" = ");
            var p = sc.AddParameterWithValue("id", DbType.Int64, entity.Id);
            sc.Query.Append(sc.MakeParameterName(p));

            await using var reader = await sc.ExecuteReaderAsync();
            var rows = await DataReaderMapper.LoadObjectsFromDataReaderAsync<IntervalEntity>(reader);

            Assert.Single(rows);
            Assert.Equal(expected, rows[0].Duration);
        });
    }
}

[Table("interval_entity")]
public class IntervalEntity
{
    [Id] [Column("id", DbType.Int64)] public long Id { get; set; }

    [Column("duration", DbType.Object)] public PostgreSqlInterval Duration { get; set; }
}
