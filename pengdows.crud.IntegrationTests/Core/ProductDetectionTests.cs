using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Every live database is detected as itself. Guards the detection probes, which were changed to
/// forms that return an empty result instead of raising a server error on other databases
/// (REV-065: PostgreSQL logs every failed probe statement by default).
/// </summary>
[Collection("IntegrationTests")]
public class ProductDetectionTests : DatabaseTestBase
{
    public ProductDetectionTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) =>
        Task.CompletedTask;

    [SkippableFact]
    public async Task Context_DetectsItsOwnDatabase()
    {
        await RunTestAgainstAllProvidersAsync((provider, context) =>
        {
            Output.WriteLine($"{provider}: detected {context.Product}");
            Assert.Equal(provider, context.Product);
            return Task.CompletedTask;
        });
    }
}
