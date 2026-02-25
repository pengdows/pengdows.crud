using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.IntegrationTests.Infrastructure;

[Collection("IntegrationTests")]
public class IntegrationTestFixtureTests
{
    private readonly IntegrationTestFixture _fixture;

    public IntegrationTestFixtureTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateDatabaseContextAsync_ReturnsSingletonPerProvider()
    {
        var provider = IntegrationTestConfiguration.EnabledProviders.First();
        var first = await _fixture.CreateDatabaseContextAsync(provider);
        var second = await _fixture.CreateDatabaseContextAsync(provider);

        Assert.Same(first, second);
    }
}