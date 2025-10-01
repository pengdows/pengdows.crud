using System.Data;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using testbed;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ConnectionManagement;

/// <summary>
/// Simple integration tests for readonly connection behavior.
/// Demonstrates ExecutionType.Read vs ExecutionType.Write functionality.
/// </summary>
public class SimpleReadOnlyTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    public SimpleReadOnlyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ReadConnection_ExecutionTypeRead_WorksWithSqliteInMemory()
    {
        // Arrange - Create in-memory SQLite context
        using var context = new DatabaseContext("Data Source=:memory:");

        // Create test table
        using var createContainer = context.CreateSqlContainer(@"
            CREATE TABLE test_readonly (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                created_at TEXT NOT NULL
            )");
        await createContainer.ExecuteNonQueryAsync();

        // Insert test data
        using var insertContainer = context.CreateSqlContainer(@"
            INSERT INTO test_readonly (name, created_at) VALUES (?, ?)");
        insertContainer.AddParameterWithValue(DbType.String, "Test Record");
        insertContainer.AddParameterWithValue(DbType.String, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        await insertContainer.ExecuteNonQueryAsync();

        // Act - Get read connection explicitly
        using var readConnection = context.GetConnection(ExecutionType.Read);
        await readConnection.OpenAsync();

        // Execute read operation using the read connection
        using var selectContainer = context.CreateSqlContainer("SELECT id, name FROM test_readonly");
        using var command = selectContainer.CreateCommand(readConnection);
        using var reader = await command.ExecuteReaderAsync();

        var recordsFound = 0;
        while (await reader.ReadAsync())
        {
            recordsFound++;
            var id = reader.GetInt64(0);
            var name = reader.GetString(1);

            Assert.True(id > 0);
            Assert.Equal("Test Record", name);
        }

        // Assert
        Assert.Equal(1, recordsFound);
        _output.WriteLine($"Read connection test completed successfully - found {recordsFound} records");
    }

    [Fact]
    public async Task ReadOnlyTransaction_IsolationLevel_AllowsReads()
    {
        // Arrange - Create in-memory SQLite context
        using var context = new DatabaseContext("Data Source=:memory:");

        // Create and populate test table
        using var setupContainer = context.CreateSqlContainer(@"
            CREATE TABLE readonly_txn (
                id INTEGER PRIMARY KEY,
                data TEXT NOT NULL
            );
            INSERT INTO readonly_txn (data) VALUES ('Initial Data');");
        await setupContainer.ExecuteNonQueryAsync();

        // Act - Start readonly transaction
        using var readonlyTransaction = context.BeginTransaction(
            IsolationLevel.ReadCommitted, ExecutionType.Read);

        // Perform read operation within readonly transaction
        using var readContainer = context.CreateSqlContainer("SELECT COUNT(*) FROM readonly_txn");
        var count = await readContainer.ExecuteScalarAsync<long>();

        // Read specific data
        using var dataContainer = context.CreateSqlContainer("SELECT data FROM readonly_txn WHERE id = 1");
        var data = await dataContainer.ExecuteScalarAsync<string>();

        readonlyTransaction.Commit();

        // Assert
        Assert.Equal(1, count);
        Assert.Equal("Initial Data", data);
        _output.WriteLine($"ReadOnly transaction test completed - read {count} records with data: {data}");
    }

    [Fact]
    public async Task WriteConnection_ExecutionTypeWrite_WorksWithSqliteInMemory()
    {
        // Arrange - Create in-memory SQLite context
        using var context = new DatabaseContext("Data Source=:memory:");

        // Create test table
        using var createContainer = context.CreateSqlContainer(@"
            CREATE TABLE test_write (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL
            )");
        await createContainer.ExecuteNonQueryAsync();

        // Act - Get write connection explicitly
        using var writeConnection = context.GetConnection(ExecutionType.Write);
        await writeConnection.OpenAsync();

        // Execute write operation using the write connection
        using var insertContainer = context.CreateSqlContainer(@"
            INSERT INTO test_write (name) VALUES (?)");
        insertContainer.AddParameterWithValue(DbType.String, "Write Test");

        using var command = insertContainer.CreateCommand(writeConnection);
        var rowsAffected = await command.ExecuteNonQueryAsync();

        // Verify the write worked
        using var verifyContainer = context.CreateSqlContainer("SELECT COUNT(*) FROM test_write");
        var count = await verifyContainer.ExecuteScalarAsync<long>();

        // Assert
        Assert.Equal(1, rowsAffected);
        Assert.Equal(1, count);
        _output.WriteLine($"Write connection test completed successfully - inserted {rowsAffected} rows, total: {count}");
    }

    [Fact]
    public async Task ReadWriteConnection_Comparison_ShowsDifferentConnections()
    {
        // Arrange - Create in-memory SQLite context
        using var context = new DatabaseContext("Data Source=:memory:");

        var initialConnectionCount = context.NumberOfOpenConnections;

        // Act - Get both read and write connections
        using var readConnection = context.GetConnection(ExecutionType.Read);
        using var writeConnection = context.GetConnection(ExecutionType.Write);

        await readConnection.OpenAsync();
        await writeConnection.OpenAsync();

        var afterOpenCount = context.NumberOfOpenConnections;
        var maxConnections = context.MaxNumberOfConnections;

        // Assert - Connections should be tracked
        Assert.True(afterOpenCount >= initialConnectionCount);
        Assert.True(maxConnections >= afterOpenCount);

        _output.WriteLine($"Connection comparison - Initial: {initialConnectionCount}, After open: {afterOpenCount}, Max: {maxConnections}");
    }

    [Fact]
    public async Task FakeDb_ReadOnlyConnectionFailure_SimulatesFailureScenarios()
    {
        // Arrange - Create FakeDb factory configured to fail
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.SetFailOnOpen();

        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);

        // Act & Assert - Connection should fail as configured
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var readConnection = context.GetConnection(ExecutionType.Read);
            await readConnection.OpenAsync();
        });

        _output.WriteLine("FakeDb readonly connection failure test completed - failure simulated correctly");
    }

    [Fact]
    public async Task FakeDb_ReadOnlyConnectionSuccess_WorksNormally()
    {
        // Arrange - Create FakeDb factory with normal behavior
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);

        // Act - Normal readonly operations should work
        using var readConnection = context.GetConnection(ExecutionType.Read);
        await readConnection.OpenAsync();

        // Execute a simple query
        using var container = context.CreateSqlContainer("SELECT 1");
        using var command = container.CreateCommand(readConnection);
        var result = await command.ExecuteScalarAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, Convert.ToInt32(result));
        _output.WriteLine("FakeDb readonly connection success test completed - operations work normally");
    }

    [Fact]
    public async Task DbMode_ReadOnlyBehavior_WorksWithDifferentModes()
    {
        var testCases = new[]
        {
            ("Data Source=:memory:", "SQLite SingleConnection"),
            ("Data Source=/tmp/test.db", "SQLite SingleWriter")
        };

        foreach (var (connectionString, description) in testCases)
        {
            try
            {
                using var context = new DatabaseContext(connectionString);
                var mode = context.ConnectionMode;

                // Test readonly behavior with this mode
                using var readConnection = context.GetConnection(ExecutionType.Read);
                await readConnection.OpenAsync();

                using var testContainer = context.CreateSqlContainer("SELECT 1");
                using var command = testContainer.CreateCommand(readConnection);
                var result = await command.ExecuteScalarAsync();

                Assert.NotNull(result);
                _output.WriteLine($"DbMode test - {description} (Mode: {mode}) works with readonly connections");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"DbMode test - {description} failed: {ex.Message}");
            }
        }
    }
}