using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for output parameter handling to ensure parameters are correctly
/// populated after command execution across different database providers.
/// </summary>
public class OutputParameterTests
{
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Firebird)]
    public async Task ExecuteNonQuery_WithOutputParameter_ReturnsOutputValue(SupportedDatabase product)
    {
        // Arrange
        var factory = new fakeDbFactory(product);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        factory.Connections.Add(connection); // For potential second connection

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // Queue the output parameter value
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?>
        {
            ["outputId"] = 42
        });
        connection.EnqueueNonQueryResult(1);

        // Act
        await using var container = context.CreateSqlContainer("EXEC my_proc {P}inputVal, {P}outputId OUTPUT");
        container.AddParameterWithValue("inputVal", DbType.String, "test");
        var outputParam = container.AddParameterWithValue("outputId", DbType.Int32, 0, ParameterDirection.Output);

        var rowsAffected = await container.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(1, rowsAffected);
        Assert.Equal(42, outputParam.Value);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Firebird)]
    public async Task ExecuteNonQuery_WithInputOutputParameter_ReturnsModifiedValue(SupportedDatabase product)
    {
        // Arrange
        var factory = new fakeDbFactory(product);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        factory.Connections.Add(connection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // Queue the output parameter value
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?>
        {
            ["counter"] = 10 // Modified from input value of 5
        });
        connection.EnqueueNonQueryResult(1);

        // Act
        await using var container = context.CreateSqlContainer("EXEC increment_counter {P}counter");
        var counterParam = container.AddParameterWithValue("counter", DbType.Int32, 5, ParameterDirection.InputOutput);

        await container.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(10, counterParam.Value);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Oracle)]
    public async Task ExecuteNonQuery_WithMultipleOutputParameters_ReturnsAllValues(SupportedDatabase product)
    {
        // Arrange
        var factory = new fakeDbFactory(product);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        factory.Connections.Add(connection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // Queue multiple output parameter values
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?>
        {
            ["outId"] = 123,
            ["outName"] = "TestResult",
            ["outCount"] = 99
        });
        connection.EnqueueNonQueryResult(1);

        // Act
        await using var container = context.CreateSqlContainer(
            "EXEC get_data {P}inputVal, {P}outId OUTPUT, {P}outName OUTPUT, {P}outCount OUTPUT");
        container.AddParameterWithValue("inputVal", DbType.Int32, 1);
        var outId = container.AddParameterWithValue("outId", DbType.Int32, 0, ParameterDirection.Output);
        var outName = container.AddParameterWithValue<string?>("outName", DbType.String, null, ParameterDirection.Output);
        var outCount = container.AddParameterWithValue("outCount", DbType.Int32, 0, ParameterDirection.Output);

        await container.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(123, outId.Value);
        Assert.Equal("TestResult", outName.Value);
        Assert.Equal(99, outCount.Value);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    public async Task ExecuteScalar_WithOutputParameter_ReturnsOutputValue(SupportedDatabase product)
    {
        // Arrange
        var factory = new fakeDbFactory(product);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        factory.Connections.Add(connection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // ExecuteScalarAsync uses ExecuteReaderAsync internally, so queue a reader result
        connection.EnqueueReaderResult(new List<Dictionary<string, object?>>
        {
            new() { ["count"] = 100 }
        });
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?>
        {
            ["rowCount"] = 50
        });

        // Act
        await using var container = context.CreateSqlContainer(
            "SELECT COUNT(*) FROM users; SET {P}rowCount = @@ROWCOUNT");
        var rowCountParam = container.AddParameterWithValue("rowCount", DbType.Int32, 0, ParameterDirection.Output);

        var scalarResult = await container.ExecuteScalarAsync<int>();

        // Assert
        Assert.Equal(100, scalarResult);
        Assert.Equal(50, rowCountParam.Value);
    }

    [Fact]
    public async Task OutputParameter_WithNullValue_ReturnsDbNull()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        factory.Connections.Add(connection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = "SqlServer",
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // Queue null output parameter value
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?>
        {
            ["nullableOutput"] = DBNull.Value
        });
        connection.EnqueueNonQueryResult(1);

        // Act
        await using var container = context.CreateSqlContainer("EXEC my_proc {P}nullableOutput OUTPUT");
        var outputParam = container.AddParameterWithValue<string?>("nullableOutput", DbType.String, null, ParameterDirection.Output);

        await container.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(DBNull.Value, outputParam.Value);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Firebird)]
    public async Task OutputParameter_AfterContainerClone_ReturnsCorrectValues(SupportedDatabase product)
    {
        // Arrange - This test verifies that cloned containers work correctly with output parameters
        var factory = new fakeDbFactory(product);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        factory.Connections.Add(connection);
        factory.Connections.Add(connection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // Create original container
        await using var originalContainer = context.CreateSqlContainer("EXEC my_proc {P}inputVal, {P}outputId OUTPUT");
        originalContainer.AddParameterWithValue("inputVal", DbType.String, "original");
        originalContainer.AddParameterWithValue("outputId", DbType.Int32, 0, ParameterDirection.Output);

        // Clone the container
        await using var clonedContainer = originalContainer.Clone(context);

        // Queue output for cloned container execution
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?>
        {
            ["outputId"] = 999
        });
        connection.EnqueueNonQueryResult(1);

        // Act - Execute the cloned container
        await clonedContainer.ExecuteNonQueryAsync();

        // Assert - The cloned container's output parameter should have the value
        var clonedOutputValue = clonedContainer.GetParameterValue<int>("outputId");
        Assert.Equal(999, clonedOutputValue);
    }

    [Fact]
    public async Task OutputParameter_MultipleExecutions_EachGetsOwnValue()
    {
        // Arrange - Verify that multiple executions with the same container get correct values
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        factory.Connections.Add(connection);
        factory.Connections.Add(connection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = "SqlServer",
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // Queue multiple sets of output values
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?> { ["seqNum"] = 1 });
        connection.EnqueueNonQueryResult(1);
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?> { ["seqNum"] = 2 });
        connection.EnqueueNonQueryResult(1);

        // Act & Assert
        await using var container = context.CreateSqlContainer("EXEC get_next_seq {P}seqNum OUTPUT");
        var seqParam = container.AddParameterWithValue("seqNum", DbType.Int32, 0, ParameterDirection.Output);

        // First execution
        await container.ExecuteNonQueryAsync();
        Assert.Equal(1, seqParam.Value);

        // Second execution - same container
        await container.ExecuteNonQueryAsync();
        Assert.Equal(2, seqParam.Value);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Oracle)]
    public async Task ReturnValueParameter_ReturnsStoredProcedureReturnValue(SupportedDatabase product)
    {
        // Arrange
        var factory = new fakeDbFactory(product);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        factory.Connections.Add(connection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // Queue return value
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?>
        {
            ["returnValue"] = 0 // Success return code
        });
        connection.EnqueueNonQueryResult(1);

        // Act
        await using var container = context.CreateSqlContainer("EXEC my_proc");
        var returnParam = container.AddParameterWithValue("returnValue", DbType.Int32, -1, ParameterDirection.ReturnValue);

        await container.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(0, returnParam.Value);
    }

    [Fact]
    public async Task OutputParameter_CaseInsensitiveMatching_FindsParameter()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        factory.Connections.Add(connection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = "SqlServer",
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // Queue output with different case than parameter name
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?>
        {
            ["OUTPUTID"] = 777 // Different case from parameter
        });
        connection.EnqueueNonQueryResult(1);

        // Act
        await using var container = context.CreateSqlContainer("EXEC my_proc {P}outputId OUTPUT");
        var outputParam = container.AddParameterWithValue("outputId", DbType.Int32, 0, ParameterDirection.Output);

        await container.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(777, outputParam.Value);
    }

    [Fact]
    public async Task InputParameter_NotModified_ByOutputParameterQueue()
    {
        // Arrange - Verify that input-only parameters are not modified
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        factory.Connections.Add(connection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = "SqlServer",
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // Queue output that includes a value for an input parameter (should be ignored)
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?>
        {
            ["inputParam"] = 999, // Should NOT modify input parameter
            ["outputParam"] = 42  // Should modify output parameter
        });
        connection.EnqueueNonQueryResult(1);

        // Act
        await using var container = context.CreateSqlContainer("EXEC my_proc {P}inputParam, {P}outputParam OUTPUT");
        var inputParam = container.AddParameterWithValue("inputParam", DbType.Int32, 100); // Input only
        var outputParam = container.AddParameterWithValue("outputParam", DbType.Int32, 0, ParameterDirection.Output);

        await container.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(100, inputParam.Value); // Input unchanged
        Assert.Equal(42, outputParam.Value); // Output modified
    }

    [Theory]
    [InlineData(DbType.Int32, 12345)]
    [InlineData(DbType.Int64, 9876543210L)]
    [InlineData(DbType.String, "Hello World")]
    [InlineData(DbType.Decimal, 123.45)]
    [InlineData(DbType.Boolean, true)]
    [InlineData(DbType.DateTime, "2024-01-15T10:30:00")]
    [InlineData(DbType.Guid, "550e8400-e29b-41d4-a716-446655440000")]
    public async Task OutputParameter_DifferentTypes_ReturnCorrectValues(DbType dbType, object expectedValue)
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        factory.Connections.Add(connection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = "SqlServer",
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // Convert string representations to actual types for DateTime and Guid
        object actualExpected = expectedValue;
        if (dbType == DbType.DateTime && expectedValue is string dateStr)
        {
            actualExpected = DateTime.Parse(dateStr);
        }
        else if (dbType == DbType.Guid && expectedValue is string guidStr)
        {
            actualExpected = Guid.Parse(guidStr);
        }

        connection.EnqueueOutputParameterResult(new Dictionary<string, object?>
        {
            ["result"] = actualExpected
        });
        connection.EnqueueNonQueryResult(1);

        // Act
        await using var container = context.CreateSqlContainer("EXEC my_proc {P}result OUTPUT");
        var resultParam = container.AddParameterWithValue("result", dbType, GetDefaultValue(dbType), ParameterDirection.Output);

        await container.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(actualExpected, resultParam.Value);
    }

    private static object? GetDefaultValue(DbType dbType)
    {
        return dbType switch
        {
            DbType.Int32 => 0,
            DbType.Int64 => 0L,
            DbType.String => null,
            DbType.Decimal => 0m,
            DbType.Boolean => false,
            DbType.DateTime => DateTime.MinValue,
            DbType.Guid => Guid.Empty,
            _ => null
        };
    }

    /// <summary>
    /// This test verifies that parameters are correctly handled when a container
    /// is reused for multiple executions - the scenario that originally required
    /// special parameter cloning for Firebird and SQL Server.
    /// </summary>
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Oracle)]
    public async Task ContainerReuse_WithOutputParameters_WorksCorrectly(SupportedDatabase product)
    {
        // Arrange
        var factory = new fakeDbFactory(product);
        var connection = new fakeDbConnection();
        // Add multiple connections for multiple executions
        for (int i = 0; i < 5; i++)
        {
            factory.Connections.Add(connection);
        }

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // Queue multiple sets of output values for sequential executions
        for (int i = 1; i <= 3; i++)
        {
            connection.EnqueueOutputParameterResult(new Dictionary<string, object?>
            {
                ["result"] = i * 10
            });
            connection.EnqueueNonQueryResult(1);
        }

        // Act - Create container once, execute multiple times
        await using var container = context.CreateSqlContainer("EXEC get_next {P}result OUTPUT");
        var resultParam = container.AddParameterWithValue("result", DbType.Int32, 0, ParameterDirection.Output);

        var results = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            await container.ExecuteNonQueryAsync();
            results.Add((int)resultParam.Value!);
        }

        // Assert - Each execution should get its own output value
        Assert.Equal(new[] { 10, 20, 30 }, results);
    }

    /// <summary>
    /// Tests that parameters in a cloned container are independent from the original.
    /// This verifies the CloneParameter implementation works correctly.
    /// </summary>
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Firebird)]
    public async Task ClonedContainer_HasIndependentParameters(SupportedDatabase product)
    {
        // Arrange
        var factory = new fakeDbFactory(product);
        var connection = new fakeDbConnection();
        for (int i = 0; i < 5; i++)
        {
            factory.Connections.Add(connection);
        }

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);

        // Create original container with output parameter
        await using var originalContainer = context.CreateSqlContainer("EXEC my_proc {P}id OUTPUT");
        var originalParam = originalContainer.AddParameterWithValue("id", DbType.Int32, 0, ParameterDirection.Output);

        // Clone the container
        await using var clonedContainer = originalContainer.Clone(context);

        // Queue different output values for each execution
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?> { ["id"] = 100 });
        connection.EnqueueNonQueryResult(1);
        connection.EnqueueOutputParameterResult(new Dictionary<string, object?> { ["id"] = 200 });
        connection.EnqueueNonQueryResult(1);

        // Act - Execute both containers
        await originalContainer.ExecuteNonQueryAsync();
        var originalResult = (int)originalParam.Value!;

        await clonedContainer.ExecuteNonQueryAsync();
        var clonedResult = clonedContainer.GetParameterValue<int>("id");

        // Assert - Each container should have its own parameter values
        Assert.Equal(100, originalResult);
        Assert.Equal(200, clonedResult);

        // Verify original wasn't modified by cloned execution
        Assert.Equal(100, originalParam.Value);
    }
}
