using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Proves that ExecuteSessionNonQuery does not need its own lock because
/// the existing transaction lifecycle guarantees serialization:
///   1. During construction — no other thread has a reference to `this`.
///   2. During completion — _completedState is atomically set to 1, which
///      causes GetLock() to throw, preventing any new DML from running
///      concurrently with the completion path (including TryResetReadOnlySession).
/// </summary>
public class TransactionSerializationTests
{
    [Fact]
    public async Task GetLock_ThrowsAfterCommit_PreventsConcurrentAccess()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);
        var tx = context.BeginTransaction();

        // Before commit, GetLock() succeeds
        var locker = tx.GetLock();
        Assert.NotNull(locker);

        tx.Commit();

        // After commit, GetLock() throws — no DML can run alongside completion
        Assert.Throws<InvalidOperationException>(() => tx.GetLock());

        tx.Dispose();
    }

    [Fact]
    public async Task GetLock_ThrowsAfterRollback_PreventsConcurrentAccess()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);
        var tx = context.BeginTransaction();

        tx.Rollback();

        // After rollback, GetLock() throws — no DML can run alongside completion
        Assert.Throws<InvalidOperationException>(() => tx.GetLock());

        tx.Dispose();
    }

    [Fact]
    public async Task CompletionLock_SerializesCompletionAttempts()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);
        var tx = context.BeginTransaction();

        tx.Commit();

        // Second completion attempt throws — proves only one completion runs
        Assert.Throws<InvalidOperationException>(() => tx.Commit());

        tx.Dispose();
    }

    [Fact]
    public async Task CompletionLock_SerializesAsyncCompletionAttempts()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);
        var tx = (TransactionContext)context.BeginTransaction();

        await tx.CommitAsync();

        // Second async completion attempt throws
        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync());

        tx.Dispose();
    }
}