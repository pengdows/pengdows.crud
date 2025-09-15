#region

using System;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TransactionContextAdditionalTests
{
    private static DatabaseContext CreateCtx() => new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}", new fakeDbFactory(SupportedDatabase.Sqlite));

    [Fact]
    public void DoubleCommit_ThrowsInvalidOperation()
    {
        using var ctx = CreateCtx();
        using var tx = ctx.BeginTransaction();
        tx.Commit();
        Assert.Throws<InvalidOperationException>(() => tx.Commit());
    }

    [Fact]
    public void DoubleRollback_ThrowsInvalidOperation()
    {
        using var ctx = CreateCtx();
        using var tx = ctx.BeginTransaction();
        tx.Rollback();
        Assert.Throws<InvalidOperationException>(() => tx.Rollback());
    }
}

