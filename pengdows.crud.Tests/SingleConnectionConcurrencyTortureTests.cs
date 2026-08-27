#region

using System;
using System.Collections.Concurrent;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// Long-running, multi-threaded torture test for the "no-op/real/reusable locker architecture" —
/// the docs/planning/future-work.md gap left after <see cref="TransactionReaderLockLifetimeTests"/> (which
/// proves one specific lock-lifetime bug is fixed, single-threaded) and
/// <c>ReusableAsyncLockerTests.ConcurrentLockAsync_SerializesAccess</c> (which stresses a bare
/// <see cref="pengdows.crud.threading.ReusableAsyncLocker"/>/counter in memory, with no real
/// connection or transaction involved). Neither exercises the actual production scenario:
/// <see cref="DbMode.SingleConnection"/>'s single shared physical connection, where
/// <c>RealAsyncLocker</c> is the only thing preventing concurrent reads and writes from reaching
/// the same provider connection/command objects at once — the failure mode most ADO.NET providers
/// actively reject or corrupt on.
/// </summary>
/// <remarks>
/// This test found two real bugs on first runs, both now fixed (see
/// <see cref="SingleConnectionConcurrentTransactionGuardTests"/> for the fix's dedicated unit
/// coverage): concurrent <c>BeginTransaction()</c> calls could race directly on the provider's own
/// transaction state (confirmed live: a raw <c>Microsoft.Data.Sqlite</c> "nested transactions"
/// exception); and, more seriously, an ordinary non-transactional write executing while another
/// task's transaction was still open could be silently absorbed into that transaction's scope and
/// rolled back with it — confirmed via a real 268-vs-252 row-count mismatch under load. Both are
/// fixed by treating a transaction as a longer-held instance of the serialization
/// <see cref="DbMode.SingleConnection"/> already applies to everything else: it holds a dedicated
/// gate for its whole lifetime, and every other caller waits its turn on that same gate (bounded
/// by <c>ModeLockTimeout</c>, not blocked forever). This test's workloads now expect contention to
/// simply serialize — readers, writers, and transactions all wait their turn rather than racing or
/// throwing.
/// </remarks>
[Collection("SqliteSerial")]
public class SingleConnectionConcurrencyTortureTests
{
    private const int ReaderCount = 8;
    private const int WriterCount = 8;
    private const int TransactionCount = 8;
    private static readonly TimeSpan ContentionWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task MixedReadWriteTransactionLoad_SerializesCorrectly_RealSqliteSingleConnection()
    {
        var mainTask = RunTortureTestAsync();
        var completed = await Task.WhenAny(mainTask, Task.Delay(OverallTimeout));

        Assert.True(
            ReferenceEquals(completed, mainTask),
            $"Torture test exceeded its {OverallTimeout.TotalSeconds}s bounded timeout — " +
            "this indicates a real deadlock regression in the locker architecture, not test flakiness.");

        await mainTask;
    }

    private static async Task RunTortureTestAsync()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<TortureRow>();

        var dbFile = Path.Combine(Path.GetTempPath(), $"crud_singleconn_torture_{Guid.NewGuid():N}.db");
        try
        {
            var cfg = new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={dbFile}",
                DbMode = DbMode.SingleConnection,
                ReadWriteMode = ReadWriteMode.ReadWrite
            };

            await using var context = new DatabaseContext(cfg, SqliteFactory.Instance, NullLoggerFactory.Instance, typeMap);
            await BuildTableAsync(context);

            var helper = new TableGateway<TortureRow, int>(context, null);
            using var stop = new CancellationTokenSource();

            var readerFailures = new ConcurrentBag<Exception>();
            var writerFailures = new ConcurrentBag<Exception>();
            var txFailures = new ConcurrentBag<Exception>();
            long readsCompleted = 0;
            long writesCompleted = 0;
            long txCommitted = 0;

            var readerTasks = Enumerable.Range(0, ReaderCount)
                .Select(_ => Task.Run(async () =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        try
                        {
                            var sc = context.CreateSqlContainer("SELECT COUNT(*) FROM TortureRows");
                            await using (var reader = await sc.ExecuteReaderAsync())
                            {
                                await reader.ReadAsync();
                                await Task.Delay(2, stop.Token);
                            }

                            Interlocked.Increment(ref readsCompleted);
                        }
                        catch (Exception ex) when (!stop.IsCancellationRequested)
                        {
                            readerFailures.Add(ex);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected once stop fires mid-operation.
                        }
                    }
                }))
                .ToArray();

            var writerTasks = Enumerable.Range(0, WriterCount)
                .Select(w => Task.Run(async () =>
                {
                    var i = 0;
                    while (!stop.IsCancellationRequested)
                    {
                        try
                        {
                            var sc = helper.BuildCreate(new TortureRow { Name = $"w{w}-{i++}" });
                            await sc.ExecuteNonQueryAsync();
                            Interlocked.Increment(ref writesCompleted);
                        }
                        catch (Exception ex) when (!stop.IsCancellationRequested)
                        {
                            writerFailures.Add(ex);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected once stop fires mid-operation.
                        }
                    }
                }))
                .ToArray();

            var transactionTasks = Enumerable.Range(0, TransactionCount)
                .Select(t => Task.Run(async () =>
                {
                    var i = 0;
                    while (!stop.IsCancellationRequested)
                    {
                        try
                        {
                            // Async on purpose: under real contention, the sync BeginTransaction()
                            // blocks the calling thread on the single-connection gate — fine for a
                            // genuinely synchronous caller, but calling it from inside an async
                            // continuation risks starving the thread pool of the threads needed to
                            // run the continuations that would release that same gate.
                            // BeginTransactionAsync() awaits instead of blocking a thread.
                            await using var tx = await context.BeginTransactionAsync();
                            await helper.CreateAsync(new TortureRow { Name = $"tx{t}-{i}" }, tx);

                            // Every 5th iteration rolls back deliberately, exercising both commit
                            // and rollback paths under concurrent load — a rolled-back row must
                            // never appear in the final count.
                            if (i % 5 == 4)
                            {
                                tx.Rollback();
                            }
                            else
                            {
                                tx.Commit();
                                Interlocked.Increment(ref txCommitted);
                            }

                            i++;
                        }
                        catch (Exception ex) when (!stop.IsCancellationRequested)
                        {
                            txFailures.Add(ex);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected once stop fires mid-operation.
                        }
                    }
                }))
                .ToArray();

            await Task.Delay(ContentionWindow);
            stop.Cancel();
            await Task.WhenAll(readerTasks.Concat(writerTasks).Concat(transactionTasks));

            // No provider-level corruption/contention exceptions ("connection already in use",
            // "reader already open," etc.) — the actual failure mode RealAsyncLocker exists to
            // prevent when many threads share one physical connection.
            Assert.Empty(readerFailures);
            Assert.Empty(writerFailures);
            Assert.Empty(txFailures);

            // Data integrity: every plain write plus every committed transaction landed, and
            // nothing else did — proves no lost writes and no rolled-back row leaking through.
            var scCount = context.CreateSqlContainer("SELECT COUNT(*) FROM TortureRows");
            var finalCount = await scCount.ExecuteScalarOrNullAsync<int>();
            Assert.Equal(Interlocked.Read(ref writesCompleted) + Interlocked.Read(ref txCommitted), finalCount);

            // Liveness: all three concurrent workloads actually made sustained progress against
            // the one shared connection, not just "eventually finished."
            Assert.True(Interlocked.Read(ref readsCompleted) > 0, "No reads completed during the contention window.");
            Assert.True(Interlocked.Read(ref writesCompleted) > 0, "No plain writes completed during the contention window.");
            Assert.True(Interlocked.Read(ref txCommitted) > 0, "No transactions committed during the contention window.");
        }
        finally
        {
            try
            {
                File.Delete(dbFile);
            }
            catch
            {
            }
        }
    }

    private static async Task BuildTableAsync(IDatabaseContext context)
    {
        var qp = context.QuotePrefix;
        var qs = context.QuoteSuffix;
        var sql = string.Format(@"CREATE TABLE IF NOT EXISTS
{0}TortureRows{1} (
    {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
    {0}Name{1} TEXT
)", qp, qs);

        var sc = context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }

    [Table("TortureRows")]
    private class TortureRow
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }
}
