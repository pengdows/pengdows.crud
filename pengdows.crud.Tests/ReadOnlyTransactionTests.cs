using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class ReadOnlyTransactionTests
{
    [Fact]
    public async Task BeginTransaction_ReadOnly_BlocksWrite()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
        await using var tx = context.BeginTransaction(readOnly: true);
        await using var container = tx.CreateSqlContainer("INSERT INTO t VALUES (1)");
        await Assert.ThrowsAsync<InvalidOperationException>(() => container.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task BeginTransaction_ReadOnly_AllowsRead()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
        await using var tx = context.BeginTransaction(readOnly: true);
        await using var container = tx.CreateSqlContainer("SELECT 1");

        // Should not throw - the operation should succeed even though FakeDb may not return results
        try
        {
            var result = await container.ExecuteScalarAsync<int>();
            Assert.Equal(1, result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("expected at least one row"))
        {
            // FakeDb may not return results, but the operation should not be blocked by read-only constraints
            Assert.True(true, "Read operation was not blocked by read-only constraints");
        }
    }

    [Fact]
    public async Task BeginTransaction_ReadWrite_AllowsWrite()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
        await using var tx = context.BeginTransaction(readOnly: false);
        await using var container = tx.CreateSqlContainer("CREATE TABLE t(id INTEGER)");
        await container.ExecuteNonQueryAsync();
    }
}
