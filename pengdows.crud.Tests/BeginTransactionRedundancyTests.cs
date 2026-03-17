using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class BeginTransactionRedundancyTests
{
    [Fact]
    public void BeginTransaction_ReadExecution_IsReadOnly()
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);

        // Now we only pass ExecutionType.Read
        using var tx = context.BeginTransaction(executionType: ExecutionType.Read);
        
        Assert.True(tx.IsReadOnlyConnection);
    }

    [Fact]
    public void BeginTransaction_WriteExecution_IsNotReadOnly()
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);

        // ExecutionType.Write is default, but making it explicit
        using var tx = context.BeginTransaction(executionType: ExecutionType.Write);

        Assert.False(tx.IsReadOnlyConnection);
    }
}
