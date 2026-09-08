#region

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TransactionSavepointsTests
{
    [Fact]
    public async Task Savepoints_Supported_DoesNotThrow()
    {
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        await using var tx = ctx.BeginTransaction(IsolationLevel.ReadCommitted);

        await tx.SavepointAsync("sp1");
        await tx.RollbackToSavepointAsync("sp1");
    }

    [Fact]
    public async Task Savepoints_Unsupported_ThrowsNotSupported()
    {
        // MySql actually supports savepoints (available since 5.0.3) and was a wrong choice of
        // "unsupported" dialect here — DuckDB (SupportsSavepoints=false) is the real thing. Also
        // updated the expectation itself: SavepointAsync/RollbackToSavepointAsync used to
        // silently no-op on an unsupported dialect; they now fail fast, matching
        // ReleaseSavepointAsync's behavior.
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.DuckDB}",
            new fakeDbFactory(SupportedDatabase.DuckDB));
        // DuckDB only supports Serializable — no explicit isolation level needed here.
        await using var tx = ctx.BeginTransaction();

        await Assert.ThrowsAsync<NotSupportedException>(() => tx.SavepointAsync("sp1").AsTask());
    }

    [Fact]
    public async Task Savepoints_Supported_AcceptsCancellationTokenOverloads()
    {
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        await using ITransactionContext tx = ctx.BeginTransaction(IsolationLevel.ReadCommitted);

        await tx.SavepointAsync("sp1", CancellationToken.None);
        await tx.RollbackToSavepointAsync("sp1", CancellationToken.None);
    }

    [Fact]
    public async Task Savepoints_Unsupported_CancellationTokenOverloadAlsoThrowsNotSupported()
    {
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.DuckDB}",
            new fakeDbFactory(SupportedDatabase.DuckDB));
        await using ITransactionContext tx = ctx.BeginTransaction();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => tx.SavepointAsync("sp1", CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ReleaseSavepointAsync_OnReleaseCapableDialect_DoesNotThrow()
    {
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        await using var tx = ctx.BeginTransaction(IsolationLevel.ReadCommitted);

        await tx.SavepointAsync("sp1");
        await tx.ReleaseSavepointAsync("sp1");
    }

    [Fact]
    public async Task ReleaseSavepointAsync_OnCreateRollbackOnlyDialect_ThrowsNotSupported()
    {
        // SQL Server's SAVE TRANSACTION has no explicit release statement at all — this must
        // throw rather than silently no-op (a no-op here would give SQL Server different
        // observable savepoint semantics than PostgreSQL/MySQL/etc. under an allegedly
        // normalized API).
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}",
            new fakeDbFactory(SupportedDatabase.SqlServer));
        await using var tx = ctx.BeginTransaction(IsolationLevel.ReadCommitted);

        await tx.SavepointAsync("sp1");

        await Assert.ThrowsAsync<NotSupportedException>(() => tx.ReleaseSavepointAsync("sp1").AsTask());
    }

    [Fact]
    public async Task ReleaseSavepointAsync_OnFullyUnsupportedDialect_ThrowsNotSupported()
    {
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.DuckDB}",
            new fakeDbFactory(SupportedDatabase.DuckDB));
        await using var tx = ctx.BeginTransaction();

        await Assert.ThrowsAsync<NotSupportedException>(() => tx.ReleaseSavepointAsync("sp1").AsTask());
    }

    [Fact]
    public async Task ReleaseSavepointAsync_AcceptsCancellationTokenOverload()
    {
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        await using ITransactionContext tx = ctx.BeginTransaction(IsolationLevel.ReadCommitted);

        await tx.SavepointAsync("sp1", CancellationToken.None);
        await tx.ReleaseSavepointAsync("sp1", CancellationToken.None);
    }
}
