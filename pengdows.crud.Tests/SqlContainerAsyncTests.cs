using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerAsyncTests
{
    [Fact]
    public async Task ExecuteScalarWriteAsync_ReturnsValue()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer("SELECT 1");

        var result = await container.ExecuteScalarWriteAsync<int>();

        Assert.Equal(42, result);
    }
}
