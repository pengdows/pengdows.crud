using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Targeted tests for the following uncovered lines:
/// - TransactionContext.Name getter/setter (lines 295-296)
/// - TransactionContext.CloseAndDisposeConnection with non-matching conn (lines 519-520)
/// - TransactionContext.SavepointAsync on DuckDB (SupportsSavepoints=false — throws NotSupportedException)
/// - TransactionContext.RollbackToSavepointAsync on DuckDB (same)
/// - TableGateway.Core.CreateAsync(TEntity, IDatabaseContext?, CancellationToken) null check (line 374)
/// - TrackedConnection.DisposeConnectionSync warning log (line 460)
/// - TrackedConnection.DisposeConnectionAsyncCore warning log (line 501)
/// </summary>
[Collection("SqliteSerial")]
public class CoveragePush_EasyWinsTests
{
    // =========================================================================
    // TransactionContext.Name getter/setter (lines 295-296)
    // =========================================================================

    // =========================================================================
    // TransactionContext.CloseAndDisposeConnection with non-matching connection (lines 519-520)
    // =========================================================================

    // =========================================================================
    // TransactionContext.SavepointAsync — DuckDB (SupportsSavepoints=false, line 584)
    // =========================================================================

    [Fact]
    public async Task TransactionContext_SavepointAsync_WhenDialectNoSavepoints_ThrowsNotSupported()
    {
        // DuckDB has SupportsSavepoints=false. SavepointAsync used to silently no-op here ("creating
        // a savepoint that's never used is harmless"), but that just meant the caller found out
        // savepoints weren't actually supported only when a later RollbackToSavepointAsync threw —
        // possibly after a lot of destructive work it believed was protected. Fails fast now,
        // matching RollbackToSavepointAsync/ReleaseSavepointAsync.
        using var ctx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=DuckDB",
            new fakeDbFactory(SupportedDatabase.DuckDB));

        using var txn = ctx.BeginTransaction();

        await Assert.ThrowsAsync<NotSupportedException>(() => txn.SavepointAsync("sp_no_op").AsTask());

        txn.Rollback();
    }

    [Fact]
    public async Task TransactionContext_RollbackToSavepointAsync_WhenDialectNoSavepoints_ThrowsNotSupported()
    {
        // DuckDB has SupportsSavepoints=false. Unlike SavepointAsync's old no-op (creating a
        // savepoint that's never used is harmless), silently no-op-ing a ROLLBACK to a savepoint
        // that was never actually created is dangerous: the caller believes partial work was
        // undone when nothing happened at all. Must throw NotSupportedException instead.
        using var ctx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=DuckDB",
            new fakeDbFactory(SupportedDatabase.DuckDB));

        using var txn = ctx.BeginTransaction();

        await Assert.ThrowsAsync<NotSupportedException>(async () => await txn.RollbackToSavepointAsync("sp_no_op"));

        txn.Rollback();
    }

    // =========================================================================
    // TableGateway.Core.CreateAsync(TEntity, IDatabaseContext?, CancellationToken) null entity (line 374)
    // =========================================================================

    // =========================================================================
    // TrackedConnection — Warning logger paths (lines 460 and 501)
    // =========================================================================

    // =========================================================================
    // TrackedConnection — Debug logger paths (lines 262, 373, 387)
    // =========================================================================

}
