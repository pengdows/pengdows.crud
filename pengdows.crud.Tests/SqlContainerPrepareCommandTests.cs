#region

using System;
using System.Data;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SqlContainerPrepareCommandTests : SqlLiteContextTestBase
{
    [Fact]
    public async Task PrepareCommandAsync_ValidCommand_PreparesSuccessfully()
    {
        using var container = Context.CreateSqlContainer("SELECT ? as Value");
        container.AddParameterWithValue(DbType.Int32, 42);

        using var connection = Context.GetConnection(ExecutionType.Read);
        using var command = container.CreateCommand(connection);

        var method = typeof(SqlContainer).GetMethod("PrepareCommandAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        await (Task)method!.Invoke(container, new object[] { command })!;

        Assert.NotNull(command);
    }

    [Fact]
    public async Task PrepareCommandAsync_WithStoredProcedure_PreparesCorrectly()
    {
        using var container = Context.CreateSqlContainer("test_procedure");
        container.AddParameterWithValue("param1", DbType.String, "test");

        using var connection = Context.GetConnection(ExecutionType.Read);
        using var command = container.CreateCommand(connection);

        try
        {
            command.CommandType = CommandType.StoredProcedure;
        }
        catch (ArgumentException ex) when (ex.Message.Contains("StoredProcedure") && ex.Message.Contains("not supported"))
        {
            // SQLite doesn't support stored procedures, skip this test
            return;
        }

        var method = typeof(SqlContainer).GetMethod("PrepareCommandAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        await (Task)method!.Invoke(container, new object[] { command })!;

        Assert.Equal(CommandType.StoredProcedure, command.CommandType);
    }
}
