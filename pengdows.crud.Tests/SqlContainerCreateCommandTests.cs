using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerCreateCommandTests : SqlLiteContextTestBase
{
    [Fact]
    public async Task CreateCommand_SetsCommandTextAndParameters()
    {
        await using var connection = Context.GetConnection(ExecutionType.Write);
        await connection.OpenAsync();

        using var container = Context.CreateSqlContainer("SELECT {P}id");
        container.AddParameterWithValue("id", DbType.Int32, 1);

        using var command = container.CreateCommand(connection);

        var expected = $"SELECT {container.MakeParameterName("id")}";
        Assert.Equal(expected, command.CommandText);
        Assert.Single(command.Parameters);

        var param = Assert.IsAssignableFrom<DbParameter>(command.Parameters[0]);
        Assert.Equal("id", param.ParameterName);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Firebird)]
    public async Task CreateCommand_DoesNotCloneParameters(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);
        await using var connection = context.GetConnection(ExecutionType.Read);
        await connection.OpenAsync();

        using var container = context.CreateSqlContainer("SELECT 1");
        var parameter = container.AddParameterWithValue("p0", DbType.Int32, 1);

        using var command = container.CreateCommand(connection);

        var cmdParam = Assert.Single(command.Parameters);
        Assert.Same(parameter, cmdParam);
    }
}
