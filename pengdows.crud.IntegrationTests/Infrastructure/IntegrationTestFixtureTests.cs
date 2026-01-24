using pengdows.crud.enums;
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
        var first = await _fixture.CreateDatabaseContextAsync(SupportedDatabase.Sqlite);
        var second = await _fixture.CreateDatabaseContextAsync(SupportedDatabase.Sqlite);

        Assert.Same(first, second);
    }
}
