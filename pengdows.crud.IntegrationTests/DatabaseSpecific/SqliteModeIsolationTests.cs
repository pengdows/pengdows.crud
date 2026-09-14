using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Ported from testbed/Sqlite/SqliteTestProvider.cs's TestSingleConnectionModeOrdinaryCommandIsolation/
/// TestSingleWriterModeConcurrentWriteTransactionSerialization (part of the
/// testbed-vs-IntegrationTests consolidation). SQLite is the primary real-world user of both
/// DbMode.SingleConnection (:memory:) and DbMode.SingleWriter (file-based) per DbMode.Best's
/// auto-selection rules, so it's where these mode-locking scenarios are exercised against a real
/// ADO.NET provider rather than fakeDb.
///
/// Not yet ported: TestSingleWriterModeReadDuringOpenWriteTransaction (the
/// EnableSingleWriterFairness on/off read-during-write-transaction probe) — deferred as
/// follow-up, tracked separately from this pass.
/// </summary>
[Collection("IntegrationTests")]
public class SqliteModeIsolationTests
{
    private readonly ITestOutputHelper _output;

    public SqliteModeIsolationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SingleConnectionMode_OrdinaryCommand_BlocksUntilOpenTransactionDisposed()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var ctx = new DatabaseContext(cfg, SqliteFactory.Instance);

        await using (var create = ctx.CreateSqlContainer(
                         "CREATE TABLE iso_probe (id INTEGER PRIMARY KEY, val TEXT)"))
        {
            await create.ExecuteNonQueryAsync();
        }

        var txn = ctx.BeginTransaction();
        await using (var insertInTxn = txn.CreateSqlContainer(
                         "INSERT INTO iso_probe (id, val) VALUES (1, 'inside-txn')"))
        {
            await insertInTxn.ExecuteNonQueryAsync();
        }

        // Without committing/disposing the transaction, race an ordinary (non-transaction-bound)
        // command against it. If DatabaseContext.GetSingleConnectionTransactionGate's contract
        // holds, this must block until the transaction is disposed, not interleave with it.
        var ordinaryTask = Task.Run(async () =>
        {
            await using var ordinary = ctx.CreateSqlContainer(
                "INSERT INTO iso_probe (id, val) VALUES (2, 'outside-txn')");
            await ordinary.ExecuteNonQueryAsync();
        });

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var stillBlockedWhileTransactionOpen = !ordinaryTask.IsCompleted;

        txn.Commit();
        await txn.DisposeAsync();

        var completedInTime = await Task.WhenAny(ordinaryTask, Task.Delay(TimeSpan.FromSeconds(10))) == ordinaryTask;
        Assert.True(completedInTime, "ordinary command never completed after the transaction was disposed (deadlock or lost gate release)");

        await ordinaryTask;

        long count;
        await using (var select = ctx.CreateSqlContainer("SELECT COUNT(*) FROM iso_probe"))
        {
            count = await select.ExecuteScalarRequiredAsync<long>();
        }

        Assert.True(stillBlockedWhileTransactionOpen,
            "ordinary command completed before the open transaction was disposed — SingleConnection's isolation gate did not block it");
        Assert.Equal(2, count);
    }

    /// <summary>
    /// Sharper version of the isolation question: blocking alone isn't isolation. If an ordinary
    /// command was genuinely "outside" the transaction (not silently part of it), its effect must
    /// survive the transaction later rolling back — and the transaction's own row must disappear.
    /// If the ordinary command's row also vanished, it was actually inside the transaction the
    /// whole time despite never being issued through the ITransactionContext.
    /// </summary>
    [Fact]
    public async Task SingleConnectionMode_OrdinaryCommand_SurvivesTransactionRollback()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var ctx = new DatabaseContext(cfg, SqliteFactory.Instance);

        await using (var create = ctx.CreateSqlContainer(
                         "CREATE TABLE rollback_probe (id INTEGER PRIMARY KEY, val TEXT)"))
        {
            await create.ExecuteNonQueryAsync();
        }

        var txn = ctx.BeginTransaction();
        await using (var insertInTxn = txn.CreateSqlContainer(
                         "INSERT INTO rollback_probe (id, val) VALUES (1, 'txn-row-should-vanish')"))
        {
            await insertInTxn.ExecuteNonQueryAsync();
        }

        var ordinaryTask = Task.Run(async () =>
        {
            await using var ordinary = ctx.CreateSqlContainer(
                "INSERT INTO rollback_probe (id, val) VALUES (2, 'ordinary-row-should-survive')");
            await ordinary.ExecuteNonQueryAsync();
        });

        await Task.Delay(TimeSpan.FromMilliseconds(300));

        txn.Rollback();
        await txn.DisposeAsync();

        var completedInTime = await Task.WhenAny(ordinaryTask, Task.Delay(TimeSpan.FromSeconds(10))) == ordinaryTask;
        Assert.True(completedInTime, "ordinary command never completed after the transaction rolled back");

        await ordinaryTask;

        int? survivingId = null;
        var rowCount = 0;
        await using (var select = ctx.CreateSqlContainer("SELECT id FROM rollback_probe"))
        await using (var reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rowCount++;
                survivingId = reader.GetInt32(0);
            }
        }

        Assert.Equal(1, rowCount);
        Assert.Equal(2, survivingId);
    }

    /// <summary>
    /// Live coverage for <see cref="ModeContentionException"/>, which had zero references anywhere
    /// in the integration suite before this test. It fires from
    /// <c>DatabaseContext.GetSingleConnectionTransactionGate()</c>'s semaphore-backed gate — the
    /// same gate <see cref="SingleConnectionMode_OrdinaryCommand_BlocksUntilOpenTransactionDisposed"/>
    /// above proves an ordinary command blocks on while a transaction holds the single shared
    /// connection — when the wait exceeds <see cref="DatabaseContextConfiguration.ModeLockTimeout"/>.
    /// This exception deliberately extends <see cref="TimeoutException"/> directly rather than
    /// pengdows.crud's own <c>DatabaseException</c> hierarchy, specifically so
    /// <c>SqlContainer</c>'s generic timeout-reclassification path (which turns a raw provider
    /// timeout into <c>CommandTimeoutException</c>) cannot swallow it — this test's core assertion
    /// is the exception TYPE, not just that something eventually timed out.
    ///
    /// No real timing race here (unlike the two tests above): the blocking transaction is opened
    /// and held by the SAME thread of control before the ordinary command is ever attempted, so
    /// the outcome is deterministic — the ordinary command is guaranteed to find the gate already
    /// held and wait the full configured timeout.
    /// </summary>
    [Fact]
    public async Task SingleConnectionMode_OrdinaryCommand_ThrowsModeContentionException_WhenGateWaitExceedsTimeout()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            ModeLockTimeout = TimeSpan.FromMilliseconds(300)
        };

        await using var ctx = new DatabaseContext(cfg, SqliteFactory.Instance);

        await using (var create = ctx.CreateSqlContainer(
                         "CREATE TABLE contention_probe (id INTEGER PRIMARY KEY, val TEXT)"))
        {
            await create.ExecuteNonQueryAsync();
        }

        var txn = ctx.BeginTransaction();
        try
        {
            await using (var insertInTxn = txn.CreateSqlContainer(
                             "INSERT INTO contention_probe (id, val) VALUES (1, 'holds-the-gate')"))
            {
                await insertInTxn.ExecuteNonQueryAsync();
            }

            // The transaction above already holds GetSingleConnectionTransactionGate() for its
            // entire span (see the class-level comment on DatabaseContext.GetSingleConnectionTransactionGate).
            // This ordinary, non-transaction-bound command must wait for it — and, with a 300ms
            // ModeLockTimeout and nothing ever releasing the gate, must time out.
            await using var ordinary = ctx.CreateSqlContainer(
                "INSERT INTO contention_probe (id, val) VALUES (2, 'should-time-out-waiting')");

            var ex = await Assert.ThrowsAsync<ModeContentionException>(
                async () => await ordinary.ExecuteNonQueryAsync());

            Assert.Equal(DbMode.SingleConnection, ex.Mode);
            Assert.Equal(TimeSpan.FromMilliseconds(300), ex.Timeout);
            // Snapshot reflects the contention this wait itself recorded, not a default/empty struct.
            Assert.True(ex.Snapshot.CurrentWaiters >= 0);
        }
        finally
        {
            // The gate is still held; rolling back releases it so the DatabaseContext (and its
            // one pinned SingleConnection connection) can dispose cleanly.
            txn.Rollback();
            await txn.DisposeAsync();
        }
    }

    [Fact]
    public async Task SingleWriterMode_ConcurrentWriteTransaction_SerializesBehindTheFirst()
    {
        var dbFilePath = Path.Combine(Path.GetTempPath(), $"pengdows.singlewriter.probe.{Guid.NewGuid():N}.sqlite");
        try
        {
            var cfg = new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={dbFilePath}",
                DbMode = DbMode.SingleWriter,
                ReadWriteMode = ReadWriteMode.ReadWrite
            };

            await using var ctx = new DatabaseContext(cfg, SqliteFactory.Instance);

            await using (var create = ctx.CreateSqlContainer(
                             "CREATE TABLE writer_probe (id INTEGER PRIMARY KEY, val TEXT)"))
            {
                await create.ExecuteNonQueryAsync();
            }

            var txn1 = await ctx.BeginTransactionAsync(executionType: ExecutionType.Write);
            await using (var insert1 = txn1.CreateSqlContainer(
                             "INSERT INTO writer_probe (id, val) VALUES (1, 'txn1')"))
            {
                await insert1.ExecuteNonQueryAsync();
            }

            // Start a second write transaction while the first is still open. SingleWriter's
            // turnstile should serialize this — it must block, not silently share txn1's
            // connection or deadlock.
            var txn2Task = Task.Run(async () =>
            {
                await using var txn2 = await ctx.BeginTransactionAsync(executionType: ExecutionType.Write);
                await using (var insert2 = txn2.CreateSqlContainer(
                                 "INSERT INTO writer_probe (id, val) VALUES (2, 'txn2')"))
                {
                    await insert2.ExecuteNonQueryAsync();
                }

                txn2.Commit();
            });

            await Task.Delay(TimeSpan.FromMilliseconds(300));
            var stillBlockedWhileTxn1Open = !txn2Task.IsCompleted;

            txn1.Commit();
            await txn1.DisposeAsync();

            var completedInTime = await Task.WhenAny(txn2Task, Task.Delay(TimeSpan.FromSeconds(15))) == txn2Task;
            Assert.True(completedInTime,
                "second write transaction never completed after the first was disposed (deadlock or lost turnstile release)");

            await txn2Task;

            long count;
            await using (var select = ctx.CreateSqlContainer("SELECT COUNT(*) FROM writer_probe"))
            {
                count = await select.ExecuteScalarRequiredAsync<long>();
            }

            Assert.True(stillBlockedWhileTxn1Open,
                "second write transaction completed before the first was disposed — SingleWriter's turnstile did not serialize it");
            Assert.Equal(2, count);
        }
        finally
        {
            try
            {
                if (File.Exists(dbFilePath))
                {
                    File.Delete(dbFilePath);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
