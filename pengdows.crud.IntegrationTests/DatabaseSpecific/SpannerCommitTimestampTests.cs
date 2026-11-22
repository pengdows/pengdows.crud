using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// B76: Spanner's commit timestamp, a TIMESTAMPTZ column declared SPANNER.COMMIT_TIMESTAMP that the server fills with the
/// transaction's commit time when the insert writes spanner.pending_commit_timestamp(). Two inserts commit at two times, and a value that
/// is not the commit-timestamp placeholder is refused for the column.
/// </summary>
[Collection("IntegrationTests")]
public sealed class SpannerCommitTimestampTests : DatabaseTestBase
{
    public SpannerCommitTimestampTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.Spanner).ToArray();

    private static async Task Exec(IDatabaseContext context, string sql)
    {
        await using var sc = context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task CommitTimestamp_IsFilledWithTheCommitTime_AndIncreases()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Spanner, async context =>
        {
            await DropTableIfExistsAsync(context, "commit_ts_doc");
            await Exec(context, "CREATE TABLE commit_ts_doc (id BIGINT PRIMARY KEY, ts SPANNER.COMMIT_TIMESTAMP)");

            var before = DateTime.UtcNow.AddMinutes(-5);
            await Exec(context, "INSERT INTO commit_ts_doc (id, ts) VALUES (1, SPANNER.PENDING_COMMIT_TIMESTAMP())");
            await Task.Delay(50);
            await Exec(context, "INSERT INTO commit_ts_doc (id, ts) VALUES (2, SPANNER.PENDING_COMMIT_TIMESTAMP())");

            await using var first = context.CreateSqlContainer("SELECT ts FROM commit_ts_doc WHERE id = 1");
            await using var second = context.CreateSqlContainer("SELECT ts FROM commit_ts_doc WHERE id = 2");
            var t1 = await first.ExecuteScalarRequiredAsync<DateTime>();
            var t2 = await second.ExecuteScalarRequiredAsync<DateTime>();
            Assert.True(t1 > before, $"commit timestamp {t1:O} is not recent");
            Assert.True(t2 > t1, $"second commit {t2:O} is not after the first {t1:O}");
        });
    }

    [SkippableFact]
    public async Task CommitTimestamp_RefusesAFutureValue()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Spanner, async context =>
        {
            await DropTableIfExistsAsync(context, "commit_ts_future_doc");
            await Exec(context, "CREATE TABLE commit_ts_future_doc (id BIGINT PRIMARY KEY, ts SPANNER.COMMIT_TIMESTAMP)");

            // A commit-timestamp column accepts the placeholder or a time that is not in the future.
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context,
                "INSERT INTO commit_ts_future_doc (id, ts) VALUES (1, TIMESTAMPTZ '2999-01-01 00:00:00+00')"));
        });
    }
}
