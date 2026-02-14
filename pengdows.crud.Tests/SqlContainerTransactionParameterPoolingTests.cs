using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("SqliteSerial")]
public class SqlContainerTransactionParameterPoolingTests : IDisposable
{
    private readonly DatabaseContext _context;
    private readonly SqliteConnection _connection;

    public SqlContainerTransactionParameterPoolingTests()
    {
        var typeMap = new TypeMapRegistry();
        var connStr = "Data Source=SqlContainerTxPoolTest;Mode=Memory;Cache=Shared";
        _context = new DatabaseContext(connStr, SqliteFactory.Instance, typeMap);

        // Keep connection open so the in-memory DB stays alive across connections.
        _connection = new SqliteConnection(connStr);
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS test_table (
                id INTEGER PRIMARY KEY,
                value INTEGER
            );
            INSERT INTO test_table (id, value) VALUES (1, 42);";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithTransactionContextAndParameters_Works()
    {
        await using var tx = _context.BeginTransaction();
        await using var container = tx.CreateSqlContainer("SELECT value FROM test_table WHERE id = ");
        container.Query.Append(container.MakeParameterName("id"));
        container.AddParameterWithValue("id", DbType.Int32, 1);

        var result = await container.ExecuteScalarAsync<long>();

        Assert.Equal(42L, result);
    }

    public void Dispose()
    {
        _connection.Dispose();
        _context.Dispose();
    }
}
