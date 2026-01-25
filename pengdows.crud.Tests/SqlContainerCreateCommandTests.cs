using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
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
}