using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Closes two loops left open after the fakeDb-based work on G6/G7b:
///
/// 1. <see cref="pengdows.crud.Tests.ReadOnlyConnectionRejectsWriteTests"/> proved the *library's*
///    wiring (fakeDb simulates a realistic SQLITE_READONLY and pengdows.crud's translator turns it
///    into <see cref="ReadOnlyViolationException"/>). It never proved the *real* SQLite engine
///    actually rejects the write -- that rested on docs/supported-databases.md's matrix, a claim,
///    not a test run here. The first test below runs the real thing.
/// 2. CONCURRENCY.md's G6 explicitly flags "whether SQLite's shared-cache locking intervenes" as
///    an Assumption: pengdows.crud's own governor admits two mismatched-intent writers
///    concurrently on shared in-memory SQLite (no library-level serialization), but does SQLite's
///    own internal lock manager still serialize the actual writes underneath, as a second line of
///    defense the library gets for free? The second and third tests answer that with two real
///    concurrent connections, deliberately bypassing pengdows.crud (SqliteConnection directly) to
///    isolate the engine's own behavior from the library's.
/// </summary>
public class SqliteReadOnlyAndSharedCacheLockingTests
{
    [SkippableFact]
    public async Task FileSqlite_SingleWriter_WriteShapedStatementViaDefaultReadIntent_IsRejectedByTheRealEngine()
    {
        Skip.IfNot(IntegrationTestConfiguration.IsEnabled(SupportedDatabase.Sqlite), "Sqlite is not enabled for this test run.");

        var dbFilePath = Path.Combine(Path.GetTempPath(), $"pengdows.g7b.real.{Guid.NewGuid():N}.sqlite");
        try
        {
            var cfg = new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={dbFilePath}",
                DbMode = DbMode.SingleWriter,
                ReadWriteMode = ReadWriteMode.ReadWrite
            };

            await using var ctx = new DatabaseContext(cfg, SqliteFactory.Instance);
            await using (var create = ctx.CreateSqlContainer("CREATE TABLE g7b_probe (id INTEGER PRIMARY KEY)"))
            {
                await create.ExecuteNonQueryAsync();
            }

            await using (var seed = ctx.CreateSqlContainer("INSERT INTO g7b_probe (id) VALUES (1)"))
            {
                await seed.ExecuteNonQueryAsync(ExecutionType.Write);
            }

            // ExecuteReaderAsync()'s default overload is ExecutionType.Read (unlike
            // ExecuteNonQueryAsync()'s, which defaults to Write) -- this is the actual
            // "forgot ExecutionType.Write" mistake G6 describes, sent against a real file-backed
            // SingleWriter context, where SqliteDialect genuinely applies Mode=ReadOnly to the
            // connection string the reader governor hands out.
            await using var write = ctx.CreateSqlContainer("UPDATE g7b_probe SET id = 2 WHERE id = 1");

            var ex = await Assert.ThrowsAsync<ReadOnlyViolationException>(
                () => write.ExecuteReaderAsync(CommandType.Text).AsTask());
            Assert.True(ex is IReadOnlyViolation);
        }
        finally
        {
            TryDelete(dbFilePath);
        }
    }

    [SkippableFact]
    public async Task SharedInMemorySqlite_SecondRealConnection_BlocksUntilTheFirstsWriteTransactionCommits()
    {
        Skip.IfNot(IntegrationTestConfiguration.IsEnabled(SupportedDatabase.Sqlite), "Sqlite is not enabled for this test run.");

        // A name unique per test run: shared-cache in-memory databases with the same name inside
        // one process are the same database, and a leftover from another test would corrupt this
        // one's row count.
        var cs = $"Data Source=file:g6-lock-probe-{Guid.NewGuid():N}?mode=memory&cache=shared";

        // Keep one connection open for the database's whole life: a shared-cache in-memory
        // database vanishes once every connection to it closes, same reason pengdows.crud's own
        // SingleWriter sentinel exists for this configuration.
        await using var keepAlive = new SqliteConnection(cs);
        await keepAlive.OpenAsync();
        await using (var create = keepAlive.CreateCommand())
        {
            create.CommandText = "CREATE TABLE lock_probe (id INTEGER PRIMARY KEY, val INTEGER)";
            await create.ExecuteNonQueryAsync();
        }

        await using (var seed = keepAlive.CreateCommand())
        {
            seed.CommandText = "INSERT INTO lock_probe (id, val) VALUES (1, 0)";
            await seed.ExecuteNonQueryAsync();
        }

        await using var writerA = new SqliteConnection(cs);
        await writerA.OpenAsync();
        await using var txnA = await writerA.BeginTransactionAsync();
        await using (var updateA = writerA.CreateCommand())
        {
            updateA.Transaction = (SqliteTransaction)txnA;
            // BEGIN (deferred) + a write statement upgrades to a RESERVED lock, which a second
            // connection's own write must wait behind -- this is SQLite's lock manager, nothing
            // pengdows.crud's governor did.
            updateA.CommandText = "UPDATE lock_probe SET val = 1 WHERE id = 1";
            await updateA.ExecuteNonQueryAsync();
        }

        await using var writerB = new SqliteConnection(cs);
        await writerB.OpenAsync();
        await using (var busyTimeout = writerB.CreateCommand())
        {
            // Long enough that "B gave up early" cannot explain a false pass, short enough the
            // test doesn't hang if the lock is never released.
            busyTimeout.CommandText = "PRAGMA busy_timeout = 5000";
            await busyTimeout.ExecuteNonQueryAsync();
        }

        var bWasBlocked = false;
        var writerBTask = Task.Run(async () =>
        {
            await using var updateB = writerB.CreateCommand();
            updateB.CommandText = "UPDATE lock_probe SET val = 2 WHERE id = 1";
            await updateB.ExecuteNonQueryAsync();
        });

        await Task.Delay(300);
        if (!writerBTask.IsCompleted)
        {
            bWasBlocked = true;
        }

        await txnA.CommitAsync();

        var completedInTime = await Task.WhenAny(writerBTask, Task.Delay(TimeSpan.FromSeconds(10))) == writerBTask;
        Assert.True(completedInTime, "Connection B's write never completed after A committed -- lock never released.");
        await writerBTask;

        // The actual resolution of the G6 Assumption: SQLite's own lock manager genuinely
        // serialized these two real writers -- B was still waiting while A's transaction was
        // open, and only finished after A committed. This is independent of, and in addition to,
        // whatever the pengdows.crud governor does or doesn't do.
        Assert.True(bWasBlocked,
            "Connection B's concurrent write completed immediately instead of waiting for " +
            "connection A's open write transaction -- SQLite's shared-cache locking did not " +
            "intervene the way this test expected.");

        await using var verify = keepAlive.CreateCommand();
        verify.CommandText = "SELECT val FROM lock_probe WHERE id = 1";
        var finalValue = (long)(await verify.ExecuteScalarAsync())!;
        Assert.Equal(2, finalValue); // B's write landed last and won, exactly once -- no corruption.
    }

    [SkippableFact]
    public async Task SharedInMemorySqlite_WriteShapedStatementViaDefaultReadIntent_StillSucceeds_NoEngineLevelReadOnlyProtection()
    {
        Skip.IfNot(IntegrationTestConfiguration.IsEnabled(SupportedDatabase.Sqlite), "Sqlite is not enabled for this test run.");

        var cs = $"Data Source=file:g7-noprotect-{Guid.NewGuid():N}?mode=memory&cache=shared";

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = cs,
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var ctx = new DatabaseContext(cfg, SqliteFactory.Instance);
        await using (var create = ctx.CreateSqlContainer("CREATE TABLE g7_probe (id INTEGER PRIMARY KEY)"))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using (var seed = ctx.CreateSqlContainer("INSERT INTO g7_probe (id) VALUES (1)"))
        {
            await seed.ExecuteNonQueryAsync(ExecutionType.Write);
        }

        // ExecuteReaderAsync()'s default is ExecutionType.Read -- the actual mistake being
        // simulated. Unlike the file-backed test above, this must NOT throw: SqliteDialect never
        // applies Mode=ReadOnly to a memory database, so neither the library nor the real
        // engine's connection-level read-only flag catches this. (SQLite's own lock manager,
        // proven above, is a *concurrency* defense against simultaneous writers -- it has
        // nothing to say about one connection's SQL not matching its declared intent.)
        await using var write = ctx.CreateSqlContainer("UPDATE g7_probe SET id = 2 WHERE id = 1");
        await using (var writeReader = await write.ExecuteReaderAsync(CommandType.Text))
        {
            // The statement ran to completion as part of opening the reader; nothing to read.
        }

        await using var verify = ctx.CreateSqlContainer("SELECT COUNT(*) FROM g7_probe WHERE id = 2");
        var count = await verify.ExecuteScalarRequiredAsync<long>();
        Assert.Equal(1, count);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
