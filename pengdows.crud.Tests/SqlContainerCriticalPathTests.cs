using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerCriticalPathTests
{
    [Fact]
    public void AddParameterWithValue_AllowsNullAssignments()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        using var container = context.CreateSqlContainer();

        var parameter = container.AddParameterWithValue("nullable", DbType.String, (string?)null);

        Assert.Equal(DBNull.Value, parameter.Value);
        Assert.Equal("nullable", parameter.ParameterName);
    }

    [Fact]
    public void Clear_RemovesSqlAndParameters()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        using var container = context.CreateSqlContainer("SELECT 1");

        container.AddParameterWithValue("p", DbType.Int32, 1);
        Assert.Equal(1, container.ParameterCount);

        container.Clear();

        Assert.Equal(0, container.ParameterCount);
        Assert.Equal(string.Empty, container.Query.ToString());
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WhenCommandCreationFails_Throws()
    {
        var factory = fakeDbFactory.CreateFailingFactory(
            SupportedDatabase.Sqlite,
            ConnectionFailureMode.FailOnCommand);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=fail-on-command.db",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer("UPDATE widgets SET name = 'test'");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await container.ExecuteNonQueryAsync());

        Assert.Contains("Simulated command creation failure", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateCommand_UsesProvidedTrackedConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        using var container = context.CreateSqlContainer("SELECT 42");

        var connection = context.GetConnection(ExecutionType.Read);
        try
        {
            using var command = container.CreateCommand(connection);
            // CreateCommand only creates the command object, CommandText is set during preparation
            Assert.NotNull(command);
            Assert.NotNull(command.Connection);
            Assert.Equal(container.ParameterCount, command.Parameters.Count);
        }
        finally
        {
            await context.CloseAndDisposeConnectionAsync(connection);
        }
    }
}
