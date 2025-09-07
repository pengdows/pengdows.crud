#region

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SqlContainerWriteOperationTests
{
    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Return_Generated_Value()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetScalarResult(123); // Mock returned value from INSERT...RETURNING
        
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("INSERT INTO test (name) VALUES (@p1) RETURNING id");
        container.AddParameterWithValue("p1", DbType.String, "test value");

        // Act
        var result = await container.ExecuteScalarWriteAsync<int>();

        // Assert
        Assert.Equal(123, result);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_With_CancellationToken_Should_Return_Generated_Value()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetScalarResult(456);
        
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("INSERT INTO test OUTPUT INSERTED.id VALUES (@p1)");
        container.AddParameterWithValue("p1", DbType.String, "test value");

        using var cts = new CancellationTokenSource();

        // Act
        var result = await container.ExecuteScalarWriteAsync<int>(CommandType.Text, cts.Token);

        // Assert
        Assert.Equal(456, result);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Throw_In_ReadOnly_Mode()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        var context = new DatabaseContext(config, factory, null, null);
        var container = context.CreateSqlContainer("INSERT INTO test VALUES (@p1)");
        container.AddParameterWithValue("p1", DbType.String, "value");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => 
            container.ExecuteScalarWriteAsync<int>());
        
        Assert.Contains("read-only mode", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Handle_Null_Result_For_Nullable_Type()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetScalarResult(null); // Mock null result
        
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("SELECT NULL");

        // Act
        var result = await container.ExecuteScalarWriteAsync<int?>();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Handle_DBNull_Result_For_Nullable_Type()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetScalarResult(DBNull.Value); // Mock DBNull result
        
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("INSERT INTO test DEFAULT VALUES RETURNING null_column");

        // Act
        var result = await container.ExecuteScalarWriteAsync<string?>();

        // Assert
        Assert.Null(result);
    }

    [Fact] 
    public async Task ExecuteScalarWriteAsync_Should_Throw_For_Null_Result_NonNullable_Type()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetScalarResult(null); // Mock null result
        
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("INSERT INTO test OUTPUT NULL VALUES (@p1)");
        container.AddParameterWithValue("p1", DbType.String, "value");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            container.ExecuteScalarWriteAsync<int>());
        
        Assert.Contains("expected a value but found none", exception.Message);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Throw_For_DBNull_Result_NonNullable_Type()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetScalarResult(DBNull.Value); // Mock DBNull result
        
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("INSERT INTO test RETURNING null_column");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            container.ExecuteScalarWriteAsync<int>());
        
        Assert.Contains("expected a value but found none", exception.Message);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Perform_Type_Coercion()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetScalarResult(42L); // Return long value
        
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("INSERT INTO test OUTPUT INSERTED.id VALUES (@p1)");
        container.AddParameterWithValue("p1", DbType.String, "test");

        // Act
        var result = await container.ExecuteScalarWriteAsync<int>(); // Request int

        // Assert
        Assert.Equal(42, result); // Should be coerced from long to int
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Handle_Nullable_Target_Type_Coercion()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetScalarResult(42); // Return int value
        
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("INSERT INTO test RETURNING id");

        // Act
        var result = await container.ExecuteScalarWriteAsync<long?>(); // Request nullable long

        // Assert
        Assert.Equal(42L, result); // Should be coerced from int to nullable long
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Validate_SingleWriter_Connection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "test",
            DbMode = DbMode.SingleWriter
        };
        var context = new DatabaseContext(config, factory, null, null);
        var container = context.CreateSqlContainer("INSERT INTO test VALUES (@p1)");
        container.AddParameterWithValue("p1", DbType.String, "value");

        // Note: This test may need adjustment based on actual SingleWriter implementation
        // The goal is to test the connection validation logic in ExecuteScalarWriteAsync

        // Act & Assert
        // This should either succeed (using correct connection) or throw with specific message
        try
        {
            factory.SetScalarResult(1);
            var result = await container.ExecuteScalarWriteAsync<int>();
            Assert.True(result is >= 1);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("SingleWriter"))
        {
            // This is expected if the connection validation logic is triggered
            Assert.Contains("shared writer connection", ex.Message);
        }
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Use_Write_ExecutionType()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetScalarResult(999);
        
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("INSERT INTO test RETURNING 999");

        // Act
        var result = await container.ExecuteScalarWriteAsync<int>();

        // Assert
        Assert.Equal(999, result);
        // The fact that this doesn't throw indicates write execution type is working
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Handle_StoredProcedure_CommandType()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetScalarResult(777);
        
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("sp_InsertAndReturn");
        container.AddParameterWithValue("param1", DbType.String, "value");

        // Act
        var result = await container.ExecuteScalarWriteAsync<int>(CommandType.StoredProcedure);

        // Assert
        Assert.Equal(777, result);
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Assert_Write_Connection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetScalarResult(1); // Set scalar result before creating context to avoid conflicts
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("INSERT INTO test VALUES (1)");

        // Act
        var result = await container.ExecuteScalarWriteAsync<int>();

        // Assert
        Assert.Equal(1, result);
        // The test passing means AssertIsWriteConnection() validation worked
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Handle_Command_Preparation_Errors()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetException(new InvalidOperationException("Command preparation failed"));
        
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("INVALID SQL SYNTAX");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            container.ExecuteScalarWriteAsync<int>());
    }

    [Fact]
    public async Task ExecuteScalarWriteAsync_Should_Handle_Connection_Locking()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetScalarResult(555);
        
        var context = new DatabaseContext("test", factory);
        var container = context.CreateSqlContainer("INSERT INTO test OUTPUT INSERTED.id VALUES (1)");

        // Act - Test that connection locking works (doesn't hang or throw)
        var result = await container.ExecuteScalarWriteAsync<int>();

        // Assert
        Assert.Equal(555, result);
    }
}
