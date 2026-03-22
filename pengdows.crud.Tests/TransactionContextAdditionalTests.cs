#region

using System;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TransactionContextAdditionalTests
{
    private static DatabaseContext CreateCtx()
    {
        return new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}",
            new fakeDbFactory(SupportedDatabase.Sqlite));
    }

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

    // -------------------------------------------------------------------------
    // AssertIsReadConnection — internal method (InternalsVisibleTo)
    // -------------------------------------------------------------------------

    [Fact]
    public void AssertIsReadConnection_OnWriteTransaction_DoesNotThrow()
    {
        using var ctx = CreateCtx();
        using var tx = (TransactionContext)ctx.BeginTransaction();

        // Normal write context has ReadWriteMode with ReadOnly bit set — should not throw
        tx.AssertIsReadConnection();
    }

    // -------------------------------------------------------------------------
    // AssertIsWriteConnection — public method; read-only tx throws (line 449)
    // -------------------------------------------------------------------------

    [Fact]
    public void AssertIsWriteConnection_OnReadOnlyTransaction_Throws()
    {
        using var ctx = CreateCtx();
        using var tx = (TransactionContext)ctx.BeginTransaction(executionType: ExecutionType.Read);

        Assert.Throws<InvalidOperationException>(() => tx.AssertIsWriteConnection());
    }

    [Fact]
    public void AssertIsWriteConnection_OnWriteTransaction_DoesNotThrow()
    {
        using var ctx = CreateCtx();
        using var tx = (TransactionContext)ctx.BeginTransaction();

        tx.AssertIsWriteConnection(); // Should not throw
    }

    // -------------------------------------------------------------------------
    // ISqlDialectProvider.Dialect — explicit interface (line 869)
    // -------------------------------------------------------------------------

    [Fact]
    public void ISqlDialectProvider_Dialect_ReturnsDialect()
    {
        using var ctx = CreateCtx();
        using var tx = ctx.BeginTransaction();

        var dialectProvider = (ISqlDialectProvider)tx;
        Assert.NotNull(dialectProvider.Dialect);
    }
}