#region

using System;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
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
/// Long-running integration-level regression coverage for SingleWriter turnstile fairness,
/// against a real, file-backed SQLite database (not fakeDb, not :memory:) via a real
/// <see cref="DatabaseContext"/> — the gap docs/planning/future-work.md called out: the turnstile
/// gate/hold/release mechanism itself is already proven deterministically at the bare
/// <see cref="pengdows.crud.infrastructure.PoolGovernor"/> level (see
/// <c>PoolGovernorFairnessTests.WriterWithTurnstile_BlocksNewReaders</c>, which asserts a gated
/// reader literally throws <see cref="OperationCanceledException"/> until the writer releases),
/// and <see cref="SingleWriterTurnstileActivationTests"/> proves the turnstile is wired up/shared
/// correctly. Neither exercises the full stack under sustained load: many concurrent writers,
/// continuously-arriving concurrent readers, a real SQLite file, over a multi-second window. This
/// test is that integration-level liveness net — it proves writers keep completing well within
/// the governor's acquire timeout under sustained reader pressure, not a from-scratch re-proof of
/// the gating mechanism itself (that's already covered, deterministically, at the unit level).
/// </summary>
[Collection("SqliteSerial")]
public class SingleWriterFairnessTortureTests
{
    private const int ReaderCount = 16;

    // PoolGovernor's turnstile admission control caps queued turnstile entrants at
    // Math.Max(maxSlots * 8, 32) per governor (see infrastructure/PoolGovernor.cs); for
    // DbMode.SingleWriter the writer governor's maxSlots is always 1, so that ceiling is a fixed
    // 32 here, and every writer unconditionally gates on the turnstile (_holdTurnstile=true).
    // WriteCount must stay safely under that ceiling -- this test's purpose is proving writers
    // make progress under sustained READER pressure (see class doc), not exercising the writer
    // governor's own turnstile queue-depth limit, which is a separate, already-correct admission
    // boundary (PoolSaturatedException is exactly its designed, intentional fast-fail response).
    // Historically this constant was 40, which happened to stay under that ceiling only because
    // of an unrelated bug (SqlContainer's connection-acquisition hot path blocking a real thread
    // per waiter instead of awaiting asynchronously) that artificially serialized how many
    // writers could ever be simultaneously in flight — fixing that bug let all writers genuinely
    // run concurrently, correctly exposing that 40 already exceeded the governor's real capacity.
    private const int WriteCount = 20;
    private const int ReadHoldMs = 5;
    private static readonly TimeSpan ContentionWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Writers_DoNotStarve_UnderSustainedConcurrentReaders_RealSqliteFile()
    {
        var mainTask = RunTortureTestAsync();
        var completed = await Task.WhenAny(mainTask, Task.Delay(OverallTimeout));

        Assert.True(
            ReferenceEquals(completed, mainTask),
            $"Torture test exceeded its {OverallTimeout.TotalSeconds}s bounded timeout — " +
            "this indicates a real writer-starvation or deadlock regression, not test flakiness.");

        // Propagate any assertion/exception from the inner task now that we know it finished.
        await mainTask;
    }

    private static async Task RunTortureTestAsync()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<TortureItem>();

        var dbFile = Path.Combine(Path.GetTempPath(), $"crud_torture_{Guid.NewGuid():N}.db");
        try
        {
            var cfg = new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={dbFile}",
                DbMode = DbMode.SingleWriter,
                ReadWriteMode = ReadWriteMode.ReadWrite,
                // Fairness is on by default (EnableSingleWriterFairness = true) — kept explicit
                // here so this test still exercises the intended path if that default ever changes.
                EnableSingleWriterFairness = true
            };

            await using var context = new DatabaseContext(cfg, SqliteFactory.Instance, NullLoggerFactory.Instance, typeMap);
            await BuildItemsTableAsync(context);

            using var readerStop = new CancellationTokenSource();
            var readsCompleted = 0L;
            var readerFailures = new ConcurrentBag<Exception>();

            var readerTasks = Enumerable.Range(0, ReaderCount)
                .Select(_ => Task.Run(async () =>
                {
                    // Continuously re-attempt reads for the whole contention window — every loop
                    // iteration is a fresh admission attempt through the governor, i.e. sustained
                    // new-reader arrivals, the scenario the turnstile gate exists to police.
                    while (!readerStop.IsCancellationRequested)
                    {
                        try
                        {
                            var sc = context.CreateSqlContainer("SELECT COUNT(*) FROM TortureItems");
                            await using (var reader = await sc.ExecuteReaderAsync())
                            {
                                await reader.ReadAsync();
                                // Brief hold before releasing, mimicking a caller iterating a
                                // result rather than an instant open/close.
                                await Task.Delay(ReadHoldMs, readerStop.Token);
                            }

                            Interlocked.Increment(ref readsCompleted);
                        }
                        catch (Exception ex) when (!readerStop.IsCancellationRequested)
                        {
                            readerFailures.Add(ex);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected once readerStop fires mid-operation.
                        }
                    }
                }))
                .ToArray();

            var helper = new TableGateway<TortureItem, int>(context, null);
            var writerLatenciesMs = new ConcurrentBag<double>();
            var writerFailures = new ConcurrentBag<Exception>();

            var writerTasks = Enumerable.Range(1, WriteCount)
                .Select(i => Task.Run(async () =>
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var sc = helper.BuildCreate(new TortureItem { Name = $"item{i}" });
                        await sc.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        writerFailures.Add(ex);
                    }
                    finally
                    {
                        sw.Stop();
                        writerLatenciesMs.Add(sw.Elapsed.TotalMilliseconds);
                    }
                }))
                .ToArray();

            var writersTask = Task.WhenAll(writerTasks);

            // Guarantee a full contention window even if 40 serialized writes against a local
            // SQLite file finish faster than that — the point is sustained overlap with the
            // continuously-looping readers, not just "writers eventually finished."
            await Task.WhenAll(writersTask, Task.Delay(ContentionWindow));

            readerStop.Cancel();
            await Task.WhenAll(readerTasks);

            Assert.Empty(writerFailures);
            Assert.Empty(readerFailures);

            // Liveness/correctness: every write actually landed — no writer was silently starved
            // out of ever completing.
            var scCount = context.CreateSqlContainer("SELECT COUNT(*) FROM TortureItems");
            var finalCount = await scCount.ExecuteScalarOrNullAsync<int>();
            Assert.Equal(WriteCount, finalCount);

            // No single writer waited anywhere near the governor's acquire timeout (default 5s)
            // under two full seconds of sustained concurrent reader load — if the turnstile were
            // failing to gate new readers, a writer could be pushed close to (or past, throwing
            // PoolSaturatedException, which would have landed in writerFailures above) that bound.
            var maxWriterLatencyMs = writerLatenciesMs.Max();
            Assert.True(
                maxWriterLatencyMs < 3000,
                $"Slowest writer took {maxWriterLatencyMs:F0}ms under sustained reader load — " +
                "too close to the governor's acquire timeout, suggesting starvation.");

            // Sanity: readers actually made sustained progress concurrently with the writers,
            // rather than being starved out themselves (fairness is meant to reduce, not
            // eliminate, reader access while a writer holds the turnstile).
            Assert.True(
                Interlocked.Read(ref readsCompleted) > 0,
                "No reads completed during the contention window.");
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

    private static async Task BuildItemsTableAsync(IDatabaseContext context)
    {
        var qp = context.QuotePrefix;
        var qs = context.QuoteSuffix;
        var sql = string.Format(@"CREATE TABLE IF NOT EXISTS
{0}TortureItems{1} (
    {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
    {0}Name{1} TEXT
)", qp, qs);

        var sc = context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }

    [Table("TortureItems")]
    private class TortureItem
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }
}
