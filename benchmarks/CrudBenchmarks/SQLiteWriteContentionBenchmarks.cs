using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Text;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.@internal;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

/// <summary>
/// THESIS PROOF: SQLite Write Contention Safety
///
/// Proves thesis points #4 and #5:
///   #4 - EF/Dapper don't protect the connection pool under heavy write contention
///        (SQLite busy_timeout=10ms causes them to throw "database is locked" exceptions)
///   #5 - pengdows degrades safely under contention: the SingleWriter governor serializes
///        writers, preventing exceptions while preserving eventual correctness.
///
/// Design: 100 concurrent writers × 50 writes per transaction, SQLite busy_timeout=10ms.
/// All three frameworks operate against the same shared-cache in-memory database.
/// Exception counts are tracked per framework in _correctnessIssues.
/// pengdows additionally tracks per-transaction commit latency for P50/P95/P99/Max analysis.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 5)]
public class SQLiteWriteContentionBenchmarks : IDisposable
{
    private const string FrameworkPengdows = "Pengdows";
    private const string FrameworkDapper = "Dapper";
    private const string FrameworkEntityFramework = "EntityFramework";
    private const string ScenarioWriteStorm = "WriteStorm";

    private const int WriteStormConcurrency = 100;
    private const int WriteStormWritesPerTransaction = 50;
    private const int BusyTimeoutMs = 10;

    private static string BusyTimeoutSql => $"PRAGMA busy_timeout={BusyTimeoutMs};";

    private DatabaseContext _pengdowsContext = null!;
    private string _connectionString = null!;
    private DbContextOptions<EfContentionContext> _efOptions = null!;
    private SqliteConnection _sentinelConnection = null!;
    private readonly ConcurrentDictionary<CorrectnessIssueKey, int> _correctnessIssues = new();

    // WHY these two bags exist (added while investigating the ~1,055 ms mean that Dapper and
    // EF converge on under this workload, and why it's nearly identical run-to-run despite
    // "Fails" varying): Microsoft.Data.Sqlite's SqliteDataReader.NextResult() retries a
    // busy/locked statement with Thread.Sleep(150) between attempts until elapsed time exceeds
    // _command.CommandTimeout * 1000ms (source: dotnet/efcore, SqliteDataReader.cs). This
    // benchmark sets DefaultTimeout=1 (1s) on the connection string, so a maximally-contended
    // statement retries ~6-7 times (~900-1050ms of blocking Thread.Sleep) before either
    // succeeding or throwing — which lines up with the observed ~1,055 ms mean almost exactly.
    // Critically, Thread.Sleep is a REAL blocking sleep even though it's reached via an
    // `await`-ed call (Microsoft.Data.Sqlite's own docs say its async methods run
    // synchronously) — so with 100 concurrent writers, this isn't just SQLite lock contention,
    // it's potential .NET thread-pool starvation from up to 100 threads blocked in Thread.Sleep
    // simultaneously. `_successTicks`/`_failedTicks` exist to test that hypothesis empirically:
    // if it's right, both should cluster near multiples of 150ms (150, 300, ..., ~900-1050),
    // not a smooth distribution — instead of just trusting the mechanism reads plausible.
    // Only Pengdows was tracked here originally; Dapper/EF now record both to make the
    // comparison direct. Bags (not framework-keyed) because each [Benchmark] method runs in
    // its own BenchmarkDotNet-spawned process with a fresh instance, so only one framework's
    // calls ever populate these in a given process — see WriteLatencySidecar for why the
    // output file is framework-scoped rather than a single shared file.
    private readonly ConcurrentBag<long> _successTicks = new();
    private readonly ConcurrentBag<long> _failedTicks = new();
    private int? _minAvailableWorkerThreads;

    // Item 9 from the independent architecture review: "Fails=0" alone doesn't prove
    // correctness — it only proves nothing was flagged as invalid, which silently degrades to
    // "the artifact recording that was unreadable" if the file goes missing (see
    // BenchmarkCorrectnessArtifactsTests). These counters are the durable postcondition the
    // review asked for: how many logical write-transactions each framework actually attempted
    // versus actually committed. A framework that catches an exception mid-transaction and moves
    // on (which is what all three of WriteStorm_Pengdows/_Dapper/_EntityFramework currently do —
    // there is no retry loop anywhere in this file) has that transaction's 50 writes genuinely
    // lost, not "eventually applied" — Attempted - Committed is exactly the count of those.
    private readonly ConcurrentDictionary<string, int> _attemptedTransactions = new();
    private readonly ConcurrentDictionary<string, int> _committedTransactions = new();

    private void MarkAttempted(string framework) =>
        _attemptedTransactions.AddOrUpdate(framework, 1, static (_, count) => count + 1);

    private void MarkCommitted(string framework) =>
        _committedTransactions.AddOrUpdate(framework, 1, static (_, count) => count + 1);

    [GlobalSetup]
    public async Task Setup()
    {
        var connStr = $"Data Source=write_contention_{Guid.NewGuid():N}.db;Mode=Memory;Cache=Shared";
        var sqliteDialect = new SqliteDialect(SqliteFactory.Instance, NullLogger<SqlDialect>.Instance);
        connStr = ConnectionPoolingConfiguration.StripUnsupportedMaxPoolSize(
            connStr,
            sqliteDialect.MaxPoolSizeSettingName);
        var builder = new SqliteConnectionStringBuilder(connStr)
        {
            DefaultTimeout = 1
        };
        _connectionString = builder.ToString();

        // Sentinel connection keeps the in-memory database alive
        _sentinelConnection = new SqliteConnection(_connectionString);
        _sentinelConnection.Open();

        var typeMap = new TypeMapRegistry();
        typeMap.Register<ContentionEntity>();

        // pengdows forces SingleWriter for SQLite (serialized writes, concurrent reads).
        // With 100 concurrent writers queuing behind 1 permit, the queue drain time
        // far exceeds the default 5 s timeout.  Use a generous timeout so pengdows
        // can demonstrate that it survives the storm while EF/Dapper accumulate failures.
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = _connectionString,
            DbMode = DbMode.Standard, // overridden to SingleWriter by SQLite dialect automatically
            ReadWriteMode = ReadWriteMode.ReadWrite,
            PoolAcquireTimeout = TimeSpan.FromMinutes(5),
            EnableMetrics = true
        };

        _pengdowsContext = new DatabaseContext(config, SqliteFactory.Instance, null, typeMap);

        _efOptions = new DbContextOptionsBuilder<EfContentionContext>()
            .UseSqlite(_connectionString)
            .Options;

        await CreateSchemaAsync();
        await SeedDataAsync();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Write correctness artifact — enables CorrectnessColumn to show failure counts
        BenchmarkCorrectnessArtifacts.Write(nameof(SQLiteWriteContentionBenchmarks),
            _correctnessIssues
                .OrderBy(pair => pair.Key.ParameterKey, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Scenario, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Framework, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Reason, StringComparer.Ordinal)
                .Select(pair => new CorrectnessIssue(
                    pair.Key.ParameterKey == "*" ? null : pair.Key.ParameterKey,
                    pair.Key.Scenario,
                    pair.Key.Framework,
                    pair.Key.Reason,
                    pair.Value))
                .ToArray());

        // Capture governor stats: PeakQueued, AvgWait, SlotTimeouts, CancelledWaits
        if (_pengdowsContext != null)
        {
            BenchmarkMetricsWriter.Write(nameof(SQLiteWriteContentionBenchmarks), _pengdowsContext);
        }

        // Write per-transaction latency sidecar
        WriteLatencySidecar();

        _pengdowsContext?.Dispose();
        _sentinelConnection?.Dispose();
    }

    private void WriteLatencySidecar()
    {
        // Each [Benchmark] method runs in its own BenchmarkDotNet-spawned process with a fresh
        // instance of this class (confirmed: "Benchmark Process NNNNN has exited" brackets each
        // method in the run log). So _attemptedTransactions only ever has ONE framework's key
        // populated in any given process — that key tells us which framework this Cleanup()
        // call belongs to. A single shared filename written with File.WriteAllText (the
        // original design) meant whichever process's Cleanup() ran LAST silently overwrote the
        // others' data — that was the actual bug behind an "Attempted: 0" row for a framework
        // that plainly ran (nonzero mean time, recorded exceptions). Writing one file per
        // framework instead makes each process's output independent and makes a genuinely
        // failed/skipped Cleanup() visible as a missing file instead of misleading zeros.
        var framework = _attemptedTransactions.Keys.FirstOrDefault() ?? "Unknown";

        static double TicksToMs(long t) => (double)t / Stopwatch.Frequency * 1000.0;

        static long Percentile(long[] sorted, double pct)
        {
            var idx = (int)Math.Ceiling(pct / 100.0 * sorted.Length) - 1;
            return sorted[Math.Max(0, Math.Min(idx, sorted.Length - 1))];
        }

        static void AppendDistribution(StringBuilder sb, string label, long[] ticks)
        {
            sb.AppendLine($"### {label} ({ticks.Length} samples)");
            sb.AppendLine();
            if (ticks.Length == 0)
            {
                sb.AppendLine("_none recorded_");
                sb.AppendLine();
                return;
            }

            Array.Sort(ticks);
            sb.AppendLine("| Percentile | Latency |");
            sb.AppendLine("|------------|---------|");
            sb.AppendLine($"| P50        | {TicksToMs(Percentile(ticks, 50)):F3} ms |");
            sb.AppendLine($"| P95        | {TicksToMs(Percentile(ticks, 95)):F3} ms |");
            sb.AppendLine($"| P99        | {TicksToMs(Percentile(ticks, 99)):F3} ms |");
            sb.AppendLine($"| Max        | {TicksToMs(ticks[^1]):F3} ms |");
            sb.AppendLine();

            // The Thread.Sleep(150)-retry hypothesis (see the field comments above
            // _successTicks/_failedTicks) predicts latencies clustering near multiples of
            // 150ms rather than a smooth spread. Report the histogram directly instead of
            // making the reader infer it from percentiles alone.
            var buckets = ticks
                .Select(t => (int)Math.Round(TicksToMs(t) / 150.0))
                .GroupBy(b => b)
                .OrderBy(g => g.Key);
            sb.AppendLine("Histogram (bucketed to nearest 150ms — the driver's retry interval):");
            sb.AppendLine();
            sb.AppendLine("| ~ms (bucket × 150) | Count |");
            sb.AppendLine("|--------------------:|------:|");
            foreach (var bucket in buckets)
            {
                sb.AppendLine($"| {bucket.Key * 150} | {bucket.Count()} |");
            }
            sb.AppendLine();
        }

        var attempted = _attemptedTransactions.GetValueOrDefault(framework);
        var committed = _committedTransactions.GetValueOrDefault(framework);
        var failureCount = _correctnessIssues
            .Where(kvp => kvp.Key.Framework == framework)
            .Sum(kvp => kvp.Value);

        var sb = new StringBuilder();
        sb.AppendLine($"# SQLiteWriteContentionBenchmarks — {framework} Transaction Latency");
        sb.AppendLine();
        sb.AppendLine("No framework in this benchmark retries a failed transaction — a caught");
        sb.AppendLine("exception aborts that transaction's 50 writes permanently, it is not");
        sb.AppendLine("retried to completion. `Attempted - Committed` is exactly how many");
        sb.AppendLine("logical write-transactions were genuinely lost.");
        sb.AppendLine();
        sb.AppendLine("| Attempted | Committed | Lost | Exception count |");
        sb.AppendLine("|----------:|----------:|-----:|-----------------:|");
        sb.AppendLine($"| {attempted} | {committed} | {attempted - committed} | {failureCount} |");
        sb.AppendLine();
        if (_minAvailableWorkerThreads.HasValue)
        {
            sb.AppendLine($"Minimum available ThreadPool worker threads observed during the storm: **{_minAvailableWorkerThreads.Value}**");
            sb.AppendLine("(a large drop from the pre-storm baseline is evidence of thread-pool");
            sb.AppendLine("starvation from Microsoft.Data.Sqlite's blocking Thread.Sleep(150) retry —");
            sb.AppendLine("see the field comment on _successTicks/_failedTicks above.)");
            sb.AppendLine();
        }
        AppendDistribution(sb, "Committed transaction latency", _successTicks.ToArray());
        AppendDistribution(sb, "Failed transaction latency (time to the caught exception)", _failedTicks.ToArray());

        try
        {
            // See BenchmarkCorrectnessArtifacts' ArtifactsDir comment: a path relative to the
            // current directory here lands in BenchmarkDotNet's generated, cleaned-up per-run
            // directory, not anywhere durable. CRUD_BENCH_ARTIFACTS_DIR (set once in
            // Program.Main, before BenchmarkSwitcher runs anything) is absolute and survives.
            var dir = Environment.GetEnvironmentVariable("CRUD_BENCH_ARTIFACTS_DIR")
                ?? Path.Combine("BenchmarkDotNet.Artifacts", "results");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{nameof(SQLiteWriteContentionBenchmarks)}-{framework}-tx-latency.md");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"[SQLiteWriteContentionBenchmarks] Wrote {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SQLiteWriteContentionBenchmarks] Failed to write latency sidecar: {ex.Message}");
        }
    }

    private async Task CreateSchemaAsync()
    {
        await using var container = _pengdowsContext.CreateSqlContainer(@"
            CREATE TABLE IF NOT EXISTS stress_test (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                value INTEGER NOT NULL
            )");
        await container.ExecuteNonQueryAsync();
    }

    private async Task SeedDataAsync()
    {
        var gateway = new TableGateway<ContentionEntity, int>(_pengdowsContext);
        for (int i = 1; i <= 100; i++)
        {
            var entity = new ContentionEntity { Value = i };
            await gateway.CreateAsync(entity);
        }
    }

    // ============================================================================
    // WriteStorm_Pengdows — SingleWriter governor serializes all 100 concurrent writers
    // ============================================================================

    [Benchmark]
    public async Task WriteStorm_Pengdows()
    {
        await RunWriteStorm(WriteStormConcurrency, async i =>
        {
            MarkAttempted(FrameworkPengdows);
            var sw = Stopwatch.StartNew();
            try
            {
                await using var tx = _pengdowsContext.BeginTransaction();
                await using (var setup = tx.CreateSqlContainer())
                {
                    await ApplyBusyTimeoutAsync(setup);
                }

                for (var j = 0; j < WriteStormWritesPerTransaction; j++)
                {
                    await using var container = tx.CreateSqlContainer();
                    container.Query.Append("UPDATE stress_test SET value = ");
                    container.Query.Append(container.MakeParameterName("value"));
                    container.Query.Append(" WHERE id = ");
                    container.Query.Append(container.MakeParameterName("id"));
                    container.AddParameterWithValue("value", DbType.Int32, (i * 1000) + j);
                    container.AddParameterWithValue("id", DbType.Int32, (j % 100) + 1);
                    var affected = await container.ExecuteNonQueryAsync();
                    if (affected != 1)
                    {
                        MarkInvalid(ScenarioWriteStorm, FrameworkPengdows,
                            $"Expected 1 row affected, got {affected}");
                    }
                }

                tx.Commit();
                sw.Stop();
                _successTicks.Add(sw.ElapsedTicks);
                MarkCommitted(FrameworkPengdows);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _failedTicks.Add(sw.ElapsedTicks);
                MarkInvalid(ScenarioWriteStorm, FrameworkPengdows, $"Exception: {ex.GetType().Name}");
            }
        });
    }

    // ============================================================================
    // WriteStorm_Dapper — per-operation SqliteConnection, no contention protection
    // ============================================================================

    [Benchmark]
    public async Task WriteStorm_Dapper()
    {
        await RunWriteStorm(WriteStormConcurrency, async i =>
        {
            MarkAttempted(FrameworkDapper);
            var sw = Stopwatch.StartNew();
            try
            {
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();
                await ApplyBusyTimeoutAsync(conn);
                await using var tx = await conn.BeginTransactionAsync();
                const string sql = "UPDATE stress_test SET value = @value WHERE id = @id";

                for (var j = 0; j < WriteStormWritesPerTransaction; j++)
                {
                    var affected = await conn.ExecuteAsync(
                        sql,
                        new { value = (i * 1000) + j, id = (j % 100) + 1 },
                        transaction: tx);

                    if (affected != 1)
                    {
                        MarkInvalid(ScenarioWriteStorm, FrameworkDapper,
                            $"Expected 1 row affected, got {affected}");
                    }
                }

                await tx.CommitAsync();
                sw.Stop();
                _successTicks.Add(sw.ElapsedTicks);
                MarkCommitted(FrameworkDapper);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _failedTicks.Add(sw.ElapsedTicks);
                MarkInvalid(ScenarioWriteStorm, FrameworkDapper, $"Exception: {ex.GetType().Name}");
            }
        });
    }

    // ============================================================================
    // WriteStorm_EntityFramework — per-operation EfContentionContext, no contention protection
    // ============================================================================

    [Benchmark]
    public async Task WriteStorm_EntityFramework()
    {
        await RunWriteStorm(WriteStormConcurrency, async i =>
        {
            MarkAttempted(FrameworkEntityFramework);
            var sw = Stopwatch.StartNew();
            try
            {
                await using var context = new EfContentionContext(_efOptions);
                await context.Database.OpenConnectionAsync();
                await ApplyBusyTimeoutAsync(context.Database.GetDbConnection());
                await using var tx = await context.Database.BeginTransactionAsync();
                const string sql = "UPDATE stress_test SET value = @value WHERE id = @id";

                for (var j = 0; j < WriteStormWritesPerTransaction; j++)
                {
                    var affected = await context.Database.ExecuteSqlRawAsync(
                        sql,
                        new SqliteParameter("value", (i * 1000) + j),
                        new SqliteParameter("id", (j % 100) + 1));

                    if (affected != 1)
                    {
                        MarkInvalid(ScenarioWriteStorm, FrameworkEntityFramework,
                            $"Expected 1 row affected, got {affected}");
                    }
                }

                await tx.CommitAsync();
                sw.Stop();
                _successTicks.Add(sw.ElapsedTicks);
                MarkCommitted(FrameworkEntityFramework);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _failedTicks.Add(sw.ElapsedTicks);
                MarkInvalid(ScenarioWriteStorm, FrameworkEntityFramework, $"Exception: {ex.GetType().Name}");
            }
        });
    }

    // ============================================================================
    // HELPERS
    // ============================================================================

    private void MarkInvalid(string scenario, string framework, string reason, string? parameterKey = null)
    {
        var normalizedParameterKey = string.IsNullOrWhiteSpace(parameterKey) ? "*" : parameterKey.Trim();
        var key = new CorrectnessIssueKey(normalizedParameterKey, scenario, framework, reason);
        _correctnessIssues.AddOrUpdate(key, 1, static (_, count) => count + 1);
    }

    private static async Task ApplyBusyTimeoutAsync(ISqlContainer container)
    {
        container.Query.Clear();
        container.Query.Append(BusyTimeoutSql);
        await container.ExecuteNonQueryAsync();
        container.Query.Clear();
    }

    private static async Task ApplyBusyTimeoutAsync(System.Data.Common.DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BusyTimeoutSql;
        await command.ExecuteNonQueryAsync();
    }

    // Not static: records min available worker threads into an instance field so
    // WriteLatencySidecar can report it. See the field comment on _successTicks/_failedTicks —
    // Microsoft.Data.Sqlite's busy-retry loop uses a real Thread.Sleep(150), so 100 concurrent
    // writers hitting contention can genuinely exhaust .NET's thread pool, not just SQLite's
    // lock. A big drop in available threads during the storm is direct, independent evidence
    // for that (as opposed to just the latency histogram, which is consistent with it but not
    // conclusive on its own).
    private async Task RunWriteStorm(int concurrency, Func<int, Task> operation)
    {
        using var startGate = new ManualResetEventSlim(false);
        using var ready = new CountdownEvent(concurrency);
        var tasks = new Task[concurrency];

        ThreadPool.GetAvailableThreads(out var availableBefore, out _);

        for (var i = 0; i < concurrency; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                ready.Signal();
                startGate.Wait();
                await operation(index);
            });
        }

        ready.Wait();
        startGate.Set();

        // Poll available worker threads while the storm is in flight to catch the trough —
        // by the time the whole batch finishes, everything has already drained back.
        var whenAll = Task.WhenAll(tasks);
        var minAvailableDuringStorm = availableBefore;
        while (!whenAll.IsCompleted)
        {
            ThreadPool.GetAvailableThreads(out var currentAvailable, out _);
            minAvailableDuringStorm = Math.Min(minAvailableDuringStorm, currentAvailable);
            await Task.Delay(10);
        }

        await whenAll;

        ThreadPool.GetAvailableThreads(out var availableAfter, out _);
        _minAvailableWorkerThreads = minAvailableDuringStorm;
        Console.WriteLine(
            $"[SQLiteWriteContentionBenchmarks] ThreadPool available workers — before: {availableBefore}, " +
            $"min during storm: {minAvailableDuringStorm}, after: {availableAfter}");
    }

    public void Dispose() => Cleanup();

    // ============================================================================
    // ENTITIES
    // ============================================================================

    [Table("stress_test")]
    public class ContentionEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("value", DbType.Int32)] public int Value { get; set; }
    }

    public class EfContentionEntity
    {
        public int Id { get; set; }
        public int Value { get; set; }
    }

    public class EfContentionContext : DbContext
    {
        public EfContentionContext(DbContextOptions<EfContentionContext> options) : base(options)
        {
        }

        public DbSet<EfContentionEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfContentionEntity>(entity =>
            {
                entity.ToTable("stress_test");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Value).HasColumnName("value");
            });
        }
    }
}
