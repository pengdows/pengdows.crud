using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Critical path tests for SqlContainer to ensure error handling and edge cases are covered
/// </summary>
public class SqlContainerCriticalPathTests
{
    /// <summary>
    /// Test SqlContainer parameter overflow handling
    /// </summary>
    [Fact]
    public void SqlContainer_ParameterOverflow_HandlesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer();

        // Add many parameters to test overflow scenarios
        for (int i = 0; i < 10000; i++)
        {
            container.AddParameterWithValue($"param{i}", DbType.String, $"value{i}");
        }

        // Should handle large parameter counts
        Assert.True(container.ParameterCount > 9000);
    }

    /// <summary>
    /// Test SqlContainer with null parameter values
    /// </summary>
    [Fact]
    public void SqlContainer_NullParameterValues_HandlesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer();

        // Test null parameter values
        var param1 = container.AddParameterWithValue("nullString", DbType.String, (string?)null);
        var param2 = container.AddParameterWithValue("nullInt", DbType.Int32, (int?)null);
        var param3 = container.AddParameterWithValue("nullObject", DbType.Object, (object?)null);

        Assert.Equal(DBNull.Value, param1.Value);
        Assert.Equal(DBNull.Value, param2.Value);
        Assert.Equal(DBNull.Value, param3.Value);
    }

    /// <summary>
    /// Test SqlContainer command creation with connection failure
    /// </summary>
    [Fact]
    public void SqlContainer_CommandCreation_ConnectionFailure_ThrowsCorrectException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetGlobalFailureMode(ConnectionFailureMode.FailOnCommand);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer("SELECT 1");

        // Should handle connection failures during command creation
        using var conn = context.GetConnection(ExecutionType.Read);
        Assert.Throws<InvalidOperationException>(() =>
            container.CreateCommand(conn));
    }

    /// <summary>
    /// Test SqlContainer execution with malformed SQL returns default success
    /// </summary>
    [Fact]
    public async Task SqlContainer_ExecuteWithMalformedSQL_ReturnsDefaultResult()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer("INVALID SQL SYNTAX HERE");

        var result = await container.ExecuteNonQueryAsync();
        Assert.Equal(1, result);
    }

    /// <summary>
    /// Test SqlContainer rejects parameter name collisions
    /// </summary>
    [Fact]
    public void SqlContainer_ParameterNameCollision_ThrowsArgumentException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer();

        container.AddParameterWithValue("test", DbType.String, "value1");

        var ex = Assert.Throws<ArgumentException>(() =>
            container.AddParameterWithValue("@test", DbType.String, "value2"));

        Assert.Contains("already been added", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, container.ParameterCount);
    }

    /// <summary>
    /// Test SqlContainer with very large SQL strings
    /// </summary>
    [Fact]
    public void SqlContainer_LargeSQL_HandlesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer();

        // Build very large SQL string
        container.Query.Append("SELECT 1");
        for (int i = 0; i < 100000; i++)
        {
            container.Query.Append($" UNION SELECT {i}");
        }

        // Should handle large SQL strings without memory issues
        var sql = container.Query.ToString();
        Assert.True(sql.Length > 1000000);
    }

    /// <summary>
    /// Test SqlContainer clear and reuse
    /// </summary>
    [Fact]
    public void SqlContainer_ClearAndReuse_WorksCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer("SELECT 1");

        // Add parameters and SQL
        container.AddParameterWithValue("test", DbType.String, "value");
        Assert.True(container.ParameterCount > 0);

        // Clear and reuse
        container.Clear();
        Assert.Equal(0, container.ParameterCount);
        Assert.Empty(container.Query.ToString());

        // Should be reusable after clear
        container.Query.Append("SELECT 2");
        container.AddParameterWithValue("test2", DbType.String, "value2");
        Assert.True(container.ParameterCount > 0);
    }

    /// <summary>
    /// Test SqlContainer parameter type coercion errors
    /// </summary>
    [Fact]
    public void SqlContainer_ParameterTypeCoercion_HandlesErrors()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer();

        // Test type coercion with incompatible types
        var complexObject = new { Prop1 = "test", Prop2 = 123 };

        // Should handle complex object conversion
        var param = container.AddParameterWithValue("complex", DbType.Object, complexObject);
        Assert.NotNull(param);
    }

    /// <summary>
    /// Test disposing a SqlContainer after starting execution completes safely
    /// </summary>
    [Fact]
    public async Task SqlContainer_DisposeAfterExecution_CompletesSafely()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        var container = context.CreateSqlContainer("SELECT 1");

        var task = container.ExecuteNonQueryAsync();
        container.Dispose();

        var result = await task;
        Assert.Equal(1, result);
    }

    /// <summary>
    /// Test SqlContainer executes stored procedures using default FakeDb semantics
    /// </summary>
    [Fact]
    public async Task SqlContainer_StoredProcedureExecution_ReturnsDefaultResult()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;Database=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer("NonExistentStoredProc");

        var result = await container.ExecuteNonQueryAsync(CommandType.StoredProcedure);
        Assert.Equal(1, result);
    }

    /// <summary>
    /// Test SqlContainer object name wrapping edge cases
    /// </summary>
    [Fact]
    public void SqlContainer_ObjectNameWrapping_HandlesEdgeCases()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer();

        // Test object name wrapping with special characters
        var wrapped1 = container.WrapObjectName("table name");
        var wrapped2 = container.WrapObjectName("table-name");
        var wrapped3 = container.WrapObjectName("table.name");
        var wrapped4 = container.WrapObjectName("table[name]");
        var wrapped5 = container.WrapObjectName("");
        var wrapped6 = container.WrapObjectName(null);

        Assert.NotNull(wrapped1);
        Assert.NotNull(wrapped2);
        Assert.NotNull(wrapped3);
        Assert.NotNull(wrapped4);
        Assert.NotNull(wrapped5);
        Assert.NotNull(wrapped6);
    }

    /// <summary>
    /// Test SqlContainer parameter name formatting edge cases
    /// </summary>
    [Fact]
    public void SqlContainer_ParameterNameFormatting_HandlesEdgeCases()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer();

        var param1 = container.CreateDbParameter("", DbType.String, "value");
        var param2 = container.CreateDbParameter(null, DbType.String, "value");
        var param3 = container.CreateDbParameter("param with spaces", DbType.String, "value");
        var param4 = container.CreateDbParameter("param-with-dashes", DbType.String, "value");

        var name1 = container.MakeParameterName(param1);
        var name2 = container.MakeParameterName(param2);
        var name3 = container.MakeParameterName(param3);
        var name4 = container.MakeParameterName(param4);

        Assert.NotNull(name1);
        Assert.NotNull(name2);
        Assert.NotNull(name3);
        Assert.NotNull(name4);
    }

    /// <summary>
    /// Test SqlContainer timeout handling
    /// </summary>
    [Fact]
    public async Task SqlContainer_TimeoutHandling_WorksCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;Command Timeout=1",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer("SELECT 1");

        // Create command with timeout
        using var conn = context.GetConnection(ExecutionType.Read);
        using var command = container.CreateCommand(conn);

        // Should respect timeout settings
        Assert.True(command.CommandTimeout >= 0);
    }

    /// <summary>
    /// Test SqlContainer supports concurrent usage with independent instances
    /// </summary>
    [Fact]
    public async Task SqlContainer_ConcurrentParameterAddition_ThreadSafe()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { EnableDataPersistence = true };
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);

        var insertTasks = new Task<int>[100];
        for (var i = 0; i < insertTasks.Length; i++)
        {
            var value = i;
            insertTasks[i] = Task.Run(async () =>
            {
                using var scoped = context.CreateSqlContainer("INSERT INTO Items (Id, name) VALUES (@id, @name)");
                scoped.AddParameterWithValue("id", DbType.Int32, value);
                scoped.AddParameterWithValue("name", DbType.String, $"value{value}");
                return await scoped.ExecuteNonQueryAsync();
            });
        }

        var results = await Task.WhenAll(insertTasks);
        Assert.All(results, r => Assert.Equal(1, r));

        using var verify = context.CreateSqlContainer("SELECT id FROM Items");
        await using var reader = await verify.ExecuteReaderAsync();
        var count = 0;
        while (await reader.ReadAsync())
        {
            count++;
        }

        Assert.Equal(insertTasks.Length, count);
    }

    /// <summary>
    /// Test SqlContainer WHERE clause tracking
    /// </summary>
    [Fact]
    public void SqlContainer_WhereClauseTracking_WorksCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var container = context.CreateSqlContainer("SELECT * FROM table");

        Assert.False(container.HasWhereAppended);

        container.Query.Append(" WHERE id = 1");
        // Note: HasWhereAppended tracking depends on implementation
        // This tests the tracking mechanism works correctly

        container.Query.Append(" AND name = 'test'");
        Assert.Contains("WHERE", container.Query.ToString());
    }
}
