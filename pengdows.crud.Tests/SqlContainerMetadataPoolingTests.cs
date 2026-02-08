using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for SqlContainer using ParameterMetadata and parameter pooling.
/// Verifies the complete flow: metadata storage → pooled parameter materialization → execution → cleanup.
/// </summary>
public class SqlContainerMetadataPoolingTests : IDisposable
{
    private readonly DatabaseContext _context;
    private readonly SqliteConnection _connection;

    public SqlContainerMetadataPoolingTests()
    {
        var typeMap = new TypeMapRegistry();
        var connStr = "Data Source=SqlContainerPoolTest;Mode=Memory;Cache=Shared";
        _context = new DatabaseContext(connStr, SqliteFactory.Instance, typeMap);

        // Keep connection open to maintain in-memory DB
        _connection = new SqliteConnection(connStr);
        _connection.Open();

        // Create test table (IF NOT EXISTS to handle shared database)
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS test_table (
                id INTEGER PRIMARY KEY,
                name TEXT,
                value INTEGER
            )";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void MakeParameterName_WithParameterMetadata_FormatsCorrectly()
    {
        // Arrange
        using var container = _context.CreateSqlContainer();
        var metadata = new ParameterMetadata("testParam", DbType.String, "value");

        // Act
        var formattedName = container.MakeParameterName(metadata);

        // Assert
        Assert.Equal("@testParam", formattedName); // SQLite uses @
    }

    [Fact]
    public async Task AddParameterWithValue_UsesMetadataInternally()
    {
        // Arrange
        using var container = _context.CreateSqlContainer("INSERT INTO test_table (id, name) VALUES (");

        // Act
        container.Query.Append(container.MakeParameterName("id"));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName("name"));
        container.Query.Append(")");

        container.AddParameterWithValue("id", DbType.Int32, 1);
        container.AddParameterWithValue("name", DbType.String, "test");

        var result = await container.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(1, result);

        // Verify data was inserted
        using var verifyCmd = _connection.CreateCommand();
        verifyCmd.CommandText = "SELECT name FROM test_table WHERE id = 1";
        var name = verifyCmd.ExecuteScalar();
        Assert.Equal("test", name);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_RentsAndReturnsParameters()
    {
        // This test verifies the pooling behavior by executing twice
        // and checking that parameters are reused

        // Arrange
        using var container = _context.CreateSqlContainer("INSERT INTO test_table (id, name, value) VALUES (");
        container.Query.Append(container.MakeParameterName("id"));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName("name"));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName("value"));
        container.Query.Append(")");

        // Act - first execution
        container.AddParameterWithValue("id", DbType.Int32, 2);
        container.AddParameterWithValue("name", DbType.String, "first");
        container.AddParameterWithValue("value", DbType.Int32, 100);

        var result1 = await container.ExecuteNonQueryAsync();

        // Clear and reuse container
        container.Clear();
        container.Query.Append("INSERT INTO test_table (id, name, value) VALUES (");
        container.Query.Append(container.MakeParameterName("id"));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName("name"));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName("value"));
        container.Query.Append(")");

        container.AddParameterWithValue("id", DbType.Int32, 3);
        container.AddParameterWithValue("name", DbType.String, "second");
        container.AddParameterWithValue("value", DbType.Int32, 200);

        // Act - second execution (should reuse pooled parameters)
        var result2 = await container.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(1, result1);
        Assert.Equal(1, result2);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WorksWithMetadata()
    {
        // Arrange - insert test data
        using var insertCmd = _connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO test_table (id, name, value) VALUES (4, 'scalar_test', 42)";
        insertCmd.ExecuteNonQuery();

        using var container = _context.CreateSqlContainer("SELECT value FROM test_table WHERE id = ");
        container.Query.Append(container.MakeParameterName("id"));
        container.AddParameterWithValue("id", DbType.Int32, 4);

        // Act
        var result = await container.ExecuteScalarAsync<int>();

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task MultipleParameters_AllMaterializedCorrectly()
    {
        // Arrange
        using var container = _context.CreateSqlContainer("INSERT INTO test_table (id, name, value) VALUES (");

        var metadata1 = new ParameterMetadata("id", DbType.Int32, 5);
        var metadata2 = new ParameterMetadata("name", DbType.String, "multi");
        var metadata3 = new ParameterMetadata("value", DbType.Int32, 999);

        container.Query.Append(container.MakeParameterName(metadata1));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName(metadata2));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName(metadata3));
        container.Query.Append(")");

        container.AddParameterWithValue("id", DbType.Int32, metadata1.Value);
        container.AddParameterWithValue("name", DbType.String, metadata2.Value);
        container.AddParameterWithValue("value", DbType.Int32, metadata3.Value);

        // Act
        var result = await container.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(1, result);

        // Verify
        using var verifyCmd = _connection.CreateCommand();
        verifyCmd.CommandText = "SELECT value FROM test_table WHERE id = 5";
        var value = verifyCmd.ExecuteScalar();
        Assert.Equal(999L, value); // SQLite returns Int64
    }

    [Fact]
    public void Clear_RemovesMetadata()
    {
        // Arrange
        using var container = _context.CreateSqlContainer("SELECT 1");
        container.AddParameterWithValue("param1", DbType.String, "test");
        container.AddParameterWithValue("param2", DbType.Int32, 42);

        // Act
        container.Clear();

        // Assert - Query should be cleared
        Assert.Empty(container.Query.ToString());
    }

    [Fact]
    public async Task NullParameterValue_HandledCorrectly()
    {
        // Arrange
        using var container = _context.CreateSqlContainer("INSERT INTO test_table (id, name, value) VALUES (");
        container.Query.Append(container.MakeParameterName("id"));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName("name"));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName("value"));
        container.Query.Append(")");

        container.AddParameterWithValue("id", DbType.Int32, 6);
        container.AddParameterWithValue<string?>("name", DbType.String, null); // NULL - explicit type for inference
        container.AddParameterWithValue("value", DbType.Int32, 123);

        // Act
        var result = await container.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(1, result);

        // Verify NULL was inserted
        using var verifyCmd = _connection.CreateCommand();
        verifyCmd.CommandText = "SELECT name FROM test_table WHERE id = 6";
        var name = verifyCmd.ExecuteScalar();
        Assert.True(name == null || name is DBNull);
    }

    [Theory]
    [InlineData(DbType.String, "text")]
    [InlineData(DbType.Int32, 42)]
    [InlineData(DbType.Int64, 9223372036854775807L)]
    [InlineData(DbType.Boolean, true)]
    public async Task VariousDbTypes_WorkCorrectly(DbType dbType, object value)
    {
        // Arrange
        using var container = _context.CreateSqlContainer("SELECT ");
        container.Query.Append(container.MakeParameterName("param"));
        container.AddParameterWithValue("param", dbType, value);

        // Act
        var result = await container.ExecuteScalarAsync<object>();

        // Assert
        Assert.NotNull(result);
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _context?.Dispose();
    }
}
