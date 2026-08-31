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
    }
}
