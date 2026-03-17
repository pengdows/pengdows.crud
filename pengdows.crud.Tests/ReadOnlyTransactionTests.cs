using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class ReadOnlyTransactionTests
{
    [Fact]
    public async Task BeginTransaction_ReadOnly_BlocksWrite()
    {
        await using var context = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance);
        await using var tx = context.BeginTransaction(executionType: ExecutionType.Read);
        await using var container = tx.CreateSqlContainer("INSERT INTO t VALUES (1)");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await container.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task BeginTransaction_ReadOnly_AllowsRead()
    {
        await using var context = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance);
        await using var tx = context.BeginTransaction(executionType: ExecutionType.Read);
        await using var container = tx.CreateSqlContainer("SELECT 1");
        var result = await container.ExecuteScalarOrNullAsync<int>();
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task BeginTransaction_ReadWrite_AllowsWrite()
    {
        await using var context = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance);
        await using var tx = context.BeginTransaction(executionType: ExecutionType.Write);
        await using var container = tx.CreateSqlContainer("CREATE TABLE t(id INTEGER)");
        await container.ExecuteNonQueryAsync();
    }
}