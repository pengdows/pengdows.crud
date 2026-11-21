using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Db2 LUW deactivates a database when its last application disconnects. A default (Best) context on Db2 LUW must
/// resolve to PreventDatabaseUnload, which keeps a sentinel connection open so the database never goes idle. If it
/// resolves to Standard, nothing holds the database active between operations.
/// </summary>
[Collection("IntegrationTests")]
public sealed class Db2SentinelModeTests : DatabaseTestBase
{
    public Db2SentinelModeTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.Db2).ToArray();

    [SkippableFact]
    public async Task DefaultContext_OnDb2Luw_ResolvesToPreventDatabaseUnload()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Db2, async context =>
        {
            Assert.Equal(DbMode.PreventDatabaseUnload, context.ConnectionMode);
            await Task.CompletedTask;
        });
    }
}
