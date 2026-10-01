using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ConnectionManagement;

/// <summary>
/// BP-311: DatabaseContext.CreateAsync runs the constructor's initialization with asynchronous
/// opens and detection probes. Against every real database it must land on exactly what the
/// constructor lands on: product, resolved DbMode, name, dialect, isolation detection and
/// PreventDatabaseUnload sentinels — and the context must work.
/// </summary>
[Collection("IntegrationTests")]
public sealed class AsyncContextCreationTests : DatabaseTestBase
{
    public AsyncContextCreationTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) =>
        Task.CompletedTask;

    [SkippableFact]
    public async Task CreateAsync_MatchesTheConstructor_OnEveryDatabase()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, fixtureContext) =>
        {
            var source = (DatabaseContext)fixtureContext;
            var config = new DatabaseContextConfiguration
            {
                ConnectionString = InternalConnectionStringAccess.GetRawConnectionString(source),
                DbMode = DbMode.Best
            };

            await using var constructed = new DatabaseContext(config, source.Factory);
            await using var created = await DatabaseContext.CreateAsync(config, source.Factory);

            Assert.Equal(constructed.Product, created.Product);
            Assert.Equal(constructed.ConnectionMode, created.ConnectionMode);
            Assert.Equal(constructed.Name, created.Name);
            Assert.Equal(constructed.Dialect.GetType(), created.Dialect.GetType());
            Assert.Equal(constructed.Dialect.DatabaseType, created.Dialect.DatabaseType);
            Assert.Equal(constructed.RCSIEnabled, created.RCSIEnabled);
            Assert.Equal(constructed.SnapshotIsolationEnabled, created.SnapshotIsolationEnabled);
            Assert.Equal(constructed.GetSentinelSnapshot().Count, created.GetSentinelSnapshot().Count);

            var versionQuery = created.Dialect.GetVersionQuery();
            if (!string.IsNullOrWhiteSpace(versionQuery))
            {
                await using var sc = created.CreateSqlContainer(versionQuery);
                Assert.NotNull(await sc.ExecuteScalarOrNullAsync<string>());
            }
        });
    }
}
