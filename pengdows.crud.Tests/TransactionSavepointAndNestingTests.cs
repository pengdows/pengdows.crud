#region

using System;
using System.Data;
using System.Threading.Tasks;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TransactionSavepointAndNestingTests : SqlLiteContextTestBase
{
    [Fact]
    public async Task Savepoint_RollbackToSavepoint_Completes()
    {
        await using var tx = Context.BeginTransaction();

        // Should not throw even if provider treats them as no-ops
        await tx.SavepointAsync("sp1");
        await tx.RollbackToSavepointAsync("sp1");

        tx.Commit();
        Assert.True(tx.WasCommitted);
        Assert.False(tx.WasRolledBack);
    }

    [Fact]
    public void NestedTransaction_Throws()
    {
        using var tx = Context.BeginTransaction();
        var asCtx = (IDatabaseContext)tx;
        Assert.Throws<InvalidOperationException>(() =>
            asCtx.BeginTransaction(IsolationLevel.ReadCommitted));
    }
}
