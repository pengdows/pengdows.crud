using Npgsql;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// A caller's own startup Options must reach read connections too. The read-only connection string
/// used to be built by appending a second <c>Options=</c> key, which replaced the caller's value
/// (confirmed live on CockroachDB: reads saw the dialect's lock_timeout, not the caller's). Reads must
/// also still run with default_transaction_read_only on.
/// </summary>
[Collection("IntegrationTests")]
public sealed class PostgreSqlFamilyReaderOptionsTests : DatabaseTestBase
{
    public PostgreSqlFamilyReaderOptionsTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders()
            .Where(p => p is SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb
                or SupportedDatabase.YugabyteDb)
            .ToArray();

    [SkippableFact]
    public async Task CallerSuppliedOptions_ReachReadConnections_AndReadsStayReadOnly()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, _) =>
        {
            var cs = Fixture.GetRawConnectionString(provider) + "Options=-c lock_timeout=120s;";
            await using var context = new DatabaseContext(cs, NpgsqlFactory.Instance);

            for (var i = 0; i < 3; i++)
            {
                await using var timeout = context.CreateSqlContainer("SHOW lock_timeout");
                var value = (await timeout.ExecuteScalarRequiredAsync<string>(ExecutionType.Read)).Trim();
                Assert.True(value is "120s" or "120000" or "120000ms" or "2min" or "00:02:00",
                    $"{provider} reader lock_timeout: {value}");

                await using var readOnly = context.CreateSqlContainer("SHOW default_transaction_read_only");
                var ro = (await readOnly.ExecuteScalarRequiredAsync<string>(ExecutionType.Read)).Trim();
                Assert.True(ro is "on" or "true", $"{provider} reader default_transaction_read_only: {ro}");
            }
        });
    }
}
