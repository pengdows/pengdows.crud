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
    private readonly ConcurrentBag<long> _transactionTicks = new();

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
        var ticks = _transactionTicks.ToArray();
        if (ticks.Length == 0) return;

        Array.Sort(ticks);

        static double TicksToMs(long t) => (double)t / Stopwatch.Frequency * 1000.0;

        long Percentile(long[] sorted, double pct)
        {
            var idx = (int)Math.Ceiling(pct / 100.0 * sorted.Length) - 1;
            return sorted[Math.Max(0, Math.Min(idx, sorted.Length - 1))];
        }

        var p50 = TicksToMs(Percentile(ticks, 50));
        var p95 = TicksToMs(Percentile(ticks, 95));
        var p99 = TicksToMs(Percentile(ticks, 99));
        var max = TicksToMs(ticks[^1]);

        var failureCount = _correctnessIssues
            .Where(kvp => kvp.Key.Framework == FrameworkPengdows)
            .Sum(kvp => kvp.Value);

        var sb = new StringBuilder();
        sb.AppendLine("# SQLiteWriteContentionBenchmarks — Pengdows Transaction Latency");
        sb.AppendLine();
        sb.AppendLine("| Percentile | Latency |");
        sb.AppendLine("|------------|---------|");
        sb.AppendLine($"| P50        | {p50:F3} ms |");
        sb.AppendLine($"| P95        | {p95:F3} ms |");
        sb.AppendLine($"| P99        | {p99:F3} ms |");
        sb.AppendLine($"| Max        | {max:F3} ms |");
        sb.AppendLine();
        sb.AppendLine($"Pengdows failure count: {failureCount} (0 = all transactions committed successfully)");

        try
        {
            var dir = Path.Combine("BenchmarkDotNet.Artifacts", "results");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{nameof(SQLiteWriteContentionBenchmarks)}-tx-latency.md");
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
                _transactionTicks.Add(sw.ElapsedTicks);
            }
            catch (Exception ex)
            {
                sw.Stop();
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
            }
            catch (Exception ex)
            {
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
            }
            catch (Exception ex)
            {
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

    private static async Task RunWriteStorm(int concurrency, Func<int, Task> operation)
    {
        using var startGate = new ManualResetEventSlim(false);
        using var ready = new CountdownEvent(concurrency);
        var tasks = new Task[concurrency];

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
        await Task.WhenAll(tasks);
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
