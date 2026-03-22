using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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

    [Fact]
    public async Task CreateCommand_EmptyQuery_ReturnsRawCommand()
    {
        await using var connection = Context.GetConnection(ExecutionType.Write);
        await connection.OpenAsync();

        using var container = Context.CreateSqlContainer();
        container.AddParameterWithValue("id", DbType.Int32, 1);

        using var command = container.CreateCommand(connection);

        Assert.Equal(string.Empty, command.CommandText);
        Assert.Empty(command.Parameters);
    }

    [Fact]
    public async Task CreateCommand_NoPlaceholder_ClearsStaleParameterSequence()
    {
        await using var connection = Context.GetConnection(ExecutionType.Write);
        await connection.OpenAsync();

        using var container = Assert.IsType<SqlContainer>(Context.CreateSqlContainer("SELECT {P}id"));
        container.AddParameterWithValue("id", DbType.Int32, 1);
        using var first = container.CreateCommand(connection);
        Assert.NotEmpty(container.ParamSequence);

        container.Query.Clear().Append("SELECT 1");
        using var second = container.CreateCommand(connection);

        Assert.Empty(container.ParamSequence);
        Assert.Equal("SELECT 1", second.CommandText);
    }
}
