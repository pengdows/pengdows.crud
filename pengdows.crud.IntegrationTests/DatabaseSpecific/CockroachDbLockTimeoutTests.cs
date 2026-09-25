using Npgsql;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// CockroachDbDialect bakes lock_timeout=30s into the Npgsql Options startup string as a safety
/// default. A caller's own explicit lock_timeout in Options must win (BP-118, 3.0 c58cb96).
/// </summary>
/// <remarks>
/// Read connections are covered by PostgreSqlFamilyReaderOptionsTests.
/// </remarks>
[Collection("IntegrationTests")]
public sealed class CockroachDbLockTimeoutTests : DatabaseTestBase
{
    public CockroachDbLockTimeoutTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.CockroachDb).ToArray();

    [SkippableFact]
    public async Task CallerSuppliedLockTimeout_IsHonoredOnWriterConnections()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.CockroachDb, async _ =>
        {
            var cs = Fixture.GetRawConnectionString(SupportedDatabase.CockroachDb) +
                     "Options=-c lock_timeout=120s;";
            await using var context = new DatabaseContext(cs, NpgsqlFactory.Instance);

            var values = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                await using var sc = context.CreateSqlContainer("SHOW lock_timeout");
                values.Add((await sc.ExecuteScalarRequiredAsync<string>(ExecutionType.Write)).Trim());
            }

            Assert.All(values, v => Assert.True(v is "120s" or "120000" or "120000ms" or "00:02:00",
                "writer lock_timeout per checkout: " + string.Join(", ", values)));
        });
    }
}
