using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace testbed;

/// <summary>
/// SQLite is the primary real-world user of both DbMode.SingleConnection (:memory:) and
/// DbMode.SingleWriter (file-based) per DbMode.Best's auto-selection rules, so it's where the
/// mode-locking scenarios in TestProvider's two new hooks are actually exercised against a real
/// ADO.NET provider rather than fakeDb.
/// </summary>
public class SqliteTestProvider : TestProvider
{
    public SqliteTestProvider(IDatabaseContext databaseContext, IServiceProvider serviceProvider)
        : base(databaseContext, serviceProvider)
    {
    }

    protected override async Task TestSingleConnectionModeOrdinaryCommandIsolation()
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
        await txn.DisposeAsync(); // releases DatabaseContext.GetSingleConnectionTransactionGate

        var completedInTime = await Task.WhenAny(ordinaryTask, Task.Delay(TimeSpan.FromSeconds(10)))
            == ordinaryTask;

        if (!completedInTime)
        {
            CheckFail("Sqlite.SingleConnectionOrdinaryCommandIsolation",
                "ordinary command never completed after the transaction was disposed (deadlock or lost gate release)");
            return;
        }

        // Propagate the background task's own exception, if any, instead of swallowing it.
        await ordinaryTask;

        long count;
        await using (var select = ctx.CreateSqlContainer("SELECT COUNT(*) FROM iso_probe"))
        {
            count = await select.ExecuteScalarRequiredAsync<long>();
        }

        if (stillBlockedWhileTransactionOpen && count == 2)
        {
            CheckOk("Sqlite.SingleConnectionOrdinaryCommandIsolation",
                "  [SingleConnectionOrdinaryCommandIsolation] Ordinary command blocked until the open transaction was disposed, then ran in isolation: OK");
        }
        else
        {
            CheckFail("Sqlite.SingleConnectionOrdinaryCommandIsolation",
                $"stillBlockedWhileTransactionOpen={stillBlockedWhileTransactionOpen}, final row count={count} (expected true/2 — the ordinary command may have silently interleaved with the open transaction instead of waiting for it)");
        }

        await TestSingleConnectionModeOrdinaryCommandSurvivesTransactionRollback();
    }

    /// <summary>
    /// Sharper version of the isolation question: blocking alone isn't isolation. If an ordinary
    /// command was genuinely "outside" the transaction (not silently part of it), its effect must
    /// survive the transaction later rolling back — and the transaction's own row must disappear.
    /// If the ordinary command's row also vanishes, it was actually inside the transaction the
    /// whole time despite never being issued through the ITransactionContext.
    /// </summary>
    private async Task TestSingleConnectionModeOrdinaryCommandSurvivesTransactionRollback()
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

        var completedInTime = await Task.WhenAny(ordinaryTask, Task.Delay(TimeSpan.FromSeconds(10)))
            == ordinaryTask;

        if (!completedInTime)
        {
            CheckFail("Sqlite.SingleConnectionOrdinaryCommandSurvivesRollback",
                "ordinary command never completed after the transaction rolled back");
            return;
        }

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

        if (rowCount == 1 && survivingId == 2)
        {
            CheckOk("Sqlite.SingleConnectionOrdinaryCommandSurvivesRollback",
                "  [SingleConnectionOrdinaryCommandSurvivesRollback] Transaction's row rolled back; ordinary command's row survived independently: OK");
        }
        else
        {
            CheckFail("Sqlite.SingleConnectionOrdinaryCommandSurvivesRollback",
                $"rowCount={rowCount}, survivingId={survivingId?.ToString() ?? "null"} (expected 1/2 — if the ordinary row also vanished, it was actually inside the transaction the whole time)");
        }
    }

    protected override async Task TestSingleWriterModeConcurrentWriteTransactionSerialization()
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

            var completedInTime = await Task.WhenAny(txn2Task, Task.Delay(TimeSpan.FromSeconds(15)))
                == txn2Task;

            if (!completedInTime)
            {
                CheckFail("Sqlite.SingleWriterConcurrentWriteTransactionSerialization",
                    "second write transaction never completed after the first was disposed (deadlock or lost turnstile release)");
                return;
            }

            await txn2Task; // propagate any exception instead of swallowing it

            long count;
            await using (var select = ctx.CreateSqlContainer("SELECT COUNT(*) FROM writer_probe"))
            {
                count = await select.ExecuteScalarRequiredAsync<long>();
            }

            if (stillBlockedWhileTxn1Open && count == 2)
            {
                CheckOk("Sqlite.SingleWriterConcurrentWriteTransactionSerialization",
                    "  [SingleWriterConcurrentWriteTransactionSerialization] Second write transaction blocked until the first was disposed, then ran to completion: OK");
            }
            else
            {
                CheckFail("Sqlite.SingleWriterConcurrentWriteTransactionSerialization",
                    $"stillBlockedWhileTxn1Open={stillBlockedWhileTxn1Open}, final row count={count} (expected true/2)");
            }
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
                // best-effort cleanup; ignore failures
            }
        }

        await TestSingleWriterModeReadDuringOpenWriteTransaction();
    }

    /// <summary>
    /// SingleWriter is documented as "governor-serialized ephemeral writer + ephemeral readers" —
    /// unlike SingleConnection's one pinned connection, reads and writes use separate connections.
    /// So unlike the write-vs-write case above (which *must* serialize — that's the mode's whole
    /// point), a read issued while a write transaction is open is the concrete case where "execute
    /// inside and outside a transaction concurrently, in any mode" should actually be achievable:
    /// the read has its own connection to use.
    ///
    /// Run twice — once with the default EnableSingleWriterFairness=true, once with it explicitly
    /// disabled — to prove *why* whatever happens happens, not just observe an outcome. Per
    /// PoolGovernor.cs's own comments, the writer-fairness turnstile makes a NEW reader gate behind
    /// any writer that is "active or waiting" for as long as that writer holds it (the whole
    /// transaction span for a writer, since holdTurnstile=true there) — a deliberate anti-starvation
    /// design, not a bug, but one whose side effect on read concurrency isn't obvious from the
    /// "ephemeral readers" phrasing alone.
    /// </summary>
    private async Task TestSingleWriterModeReadDuringOpenWriteTransaction()
    {
        await RunReadDuringOpenWriteTransactionProbe(enableFairness: true);
        await RunReadDuringOpenWriteTransactionProbe(enableFairness: false);
    }

    private async Task RunReadDuringOpenWriteTransactionProbe(bool enableFairness)
    {
        var checkName = enableFairness
            ? "Sqlite.SingleWriterReadDuringOpenWriteTransaction.FairnessOn"
            : "Sqlite.SingleWriterReadDuringOpenWriteTransaction.FairnessOff";
        var dbFilePath = Path.Combine(Path.GetTempPath(), $"pengdows.singlewriter.read.probe.{Guid.NewGuid():N}.sqlite");
        try
        {
            var cfg = new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={dbFilePath}",
                DbMode = DbMode.SingleWriter,
                ReadWriteMode = ReadWriteMode.ReadWrite,
                EnableSingleWriterFairness = enableFairness
            };

            await using var ctx = new DatabaseContext(cfg, SqliteFactory.Instance);

            await using (var create = ctx.CreateSqlContainer(
                "CREATE TABLE read_probe (id INTEGER PRIMARY KEY, val TEXT)"))
            {
                await create.ExecuteNonQueryAsync();
            }
            await using (var seed = ctx.CreateSqlContainer(
                "INSERT INTO read_probe (id, val) VALUES (100, 'seed')"))
            {
                await seed.ExecuteNonQueryAsync();
            }

            var txn = await ctx.BeginTransactionAsync(executionType: ExecutionType.Write);
            await using (var insertInTxn = txn.CreateSqlContainer(
                "INSERT INTO read_probe (id, val) VALUES (1, 'uncommitted')"))
            {
                await insertInTxn.ExecuteNonQueryAsync();
            }

            Exception? readException = null;
            var readSw = System.Diagnostics.Stopwatch.StartNew();
            var readTask = Task.Run(async () =>
            {
                try
                {
                    await using var select = ctx.CreateSqlContainer("SELECT COUNT(*) FROM read_probe");
                    return await select.ExecuteScalarRequiredAsync<long>(ExecutionType.Read, CommandType.Text);
                }
                catch (Exception ex)
                {
                    readException = ex;
                    return -1L;
                }
            });

            var completedQuickly = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromMilliseconds(500)))
                == readTask;
            readSw.Stop();

            if (!completedQuickly)
            {
                // The read didn't finish within 500ms while the write transaction was still open —
                // it's serialized/blocked, not concurrent. Wait for it to actually resolve so the
                // read side of the test doesn't leak, then report the finding either way.
                txn.Commit();
                await txn.DisposeAsync();
                var finished = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(10))) == readTask;
                if (!finished)
                {
                    CheckFail(checkName,
                        "read never completed even after the write transaction was disposed (deadlock)");
                    return;
                }

                if (enableFairness)
                {
                    CheckOk(checkName,
                        $"  [{checkName}] Read did NOT run concurrently with the open write transaction — it blocked until disposal ({readSw.ElapsedMilliseconds}ms). Not a correctness bug (isolation still holds), and matches PoolGovernor's documented writer-fairness turnstile behavior.");
                }
                else
                {
                    CheckFail(checkName,
                        $"read still blocked ({readSw.ElapsedMilliseconds}ms) even with EnableSingleWriterFairness=false — the turnstile is not the (sole) cause of the serialization seen with fairness on; something else is also gating reads behind an open write transaction");
                }

                return;
            }

            txn.Rollback();
            await txn.DisposeAsync();

            if (readException != null)
            {
                CheckFail(checkName,
                    $"read ran concurrently but threw: {readException.GetType().Name}: {readException.Message}");
                return;
            }

            var readResult = await readTask;

            if (!enableFairness)
            {
                CheckOk(checkName,
                    $"  [{checkName}] With EnableSingleWriterFairness=false, the read ran CONCURRENTLY with the open write transaction in {readSw.ElapsedMilliseconds}ms, saw count={readResult} (pre-transaction snapshot) — confirms the writer-fairness turnstile (not something else) is what serializes reads behind an open writer when fairness is on.");
            }
            else
            {
                CheckFail(checkName,
                    $"read ran concurrently ({readSw.ElapsedMilliseconds}ms) even with EnableSingleWriterFairness=true (the default) — expected it to block per PoolGovernor's documented turnstile gating; the default behavior may have changed");
            }
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
                // best-effort cleanup; ignore failures
            }
        }
    }
}
