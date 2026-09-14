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

    /// <summary>
    /// The SingleConnection/SingleWriter mode-locking scenarios themselves are now covered by
    /// pengdows.crud.IntegrationTests/DatabaseSpecific/SqliteModeIsolationTests.cs (ported from
    /// this class's former overrides). The fairness-variant sub-test below was deliberately NOT
    /// ported (see that file's own remarks) and is preserved here as the one still-unique check.
    /// </summary>
    protected override async Task RunAdditionalTestsAsync()
    {
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
