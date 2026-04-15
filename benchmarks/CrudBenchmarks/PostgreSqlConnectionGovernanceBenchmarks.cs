using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using pengdows.stormgate;

namespace CrudBenchmarks;

/// <summary>
/// THESIS PROOF: PostgreSQL connection governance under overload.
///
/// Demonstrates what happens when application concurrency exceeds the database
/// server's max_connections limit — and how StormGate prevents the crash.
///
/// The postgres container is started with max_connections=25.
/// All benchmarks run 200 operations at 100-way parallelism — 4× the server limit.
///
/// Without a connection governor:
///   - Dapper: the Npgsql pool (max 100) attempts to open 100 physical connections.
///     PostgreSQL rejects any beyond 25 with error 53300 "sorry, too many clients already".
///     Result: immediate exception storm → benchmark fails → NA.
///   - EF Core: same pool behaviour, same crash.
///
/// With StormGate (20 permits, well below the server's 25-connection limit):
///   - At most 20 concurrent connection opens ever reach the server.
///   - The remaining 80 concurrent tasks wait in the semaphore queue.
///   - Zero rejected connections, zero errors, measured throughput with latency numbers.
///
/// The message: one NuGet package and three lines of setup is the difference
/// between "crashes under load" and "queues and completes".
/// </summary>
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3, invocationCount: 5)]
public class PostgreSqlConnectionGovernanceBenchmarks : IAsyncDisposable
{
    private const string FrameworkDapper = "Dapper";
    private const string FrameworkStormGate = "StormGate";
    private const string FrameworkEntityFramework = "EntityFramework";
    private const string ScenarioUncontrolled = "Uncontrolled";
    private const string ScenarioGoverned = "Governed";
    private const int PgMaxConnections = 25;   // deliberately low — below default Npgsql pool max
    private const int StormGatePermits = 20;   // well under server limit; tasks queue, not crash
    private const int Parallelism = 100;       // 4× the server limit — enough to saturate it
    private const int OperationsPerRun = 200;

    private static readonly string PgPassword = GeneratePassword();

    private static string GeneratePassword()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(18);
        return Convert.ToBase64String(bytes);
    }

    private IContainer? _container;
    private string _connStr = string.Empty;

    private NpgsqlDataSource _dapperDataSource = null!;  // pool max 100 — exceeds server limit
    private StormGate _stormGate = null!;                // 20 permits — stays under server limit
    private DbContextOptions<GovEfDbContext> _efOptions = null!;

    private string _querySql = null!;
    private readonly ConcurrentDictionary<CorrectnessIssueKey, int> _correctnessIssues = new();
    private readonly ConcurrentBag<long> _stormGateLatencyTicks = new();

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", PgPassword)
            .WithEnvironment("POSTGRES_DB", "gov_test")
            .WithPortBinding(0, 5432)
            // Key to the demo: max_connections well below the default Npgsql pool size (100).
            // Unprotected clients will try to open 100 physical connections; postgres rejects
            // anything beyond 25.  StormGate's permit count keeps opens at 20 — safe.
            .WithCommand("-c", $"max_connections={PgMaxConnections}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _container.StartAsync();

        var port = _container.GetMappedPublicPort(5432);
        _connStr = $"Host=localhost;Port={port};Database=gov_test;Username=postgres;Password={PgPassword}";

        // Create the shared data source first so seeding uses the same pool as benchmarks.
        _dapperDataSource = NpgsqlDataSource.Create(_connStr);

        await WaitForReadyAsync();
        await SeedAsync();

        _stormGate = new StormGate(_dapperDataSource, StormGatePermits, TimeSpan.FromSeconds(30));

        _efOptions = new DbContextOptionsBuilder<GovEfDbContext>()
            .UseNpgsql(_connStr)
            .Options;

        _querySql = "SELECT id, val FROM gov_items WHERE id = 1";

        Console.WriteLine($"[GOV] postgres max_connections={PgMaxConnections}, " +
                          $"StormGate permits={StormGatePermits}, " +
                          $"test parallelism={Parallelism}");
    }

    // ── Uncontrolled Dapper ───────────────────────────────────────────────────
    // Npgsql pool max = 100, postgres max_connections = 25.
    // Pool attempts to open 100 physical connections → postgres rejects at 26.
    // Expected result: NA (exception storm from 53300 "too many clients already").

    [Benchmark]
    public async Task Dapper_Uncontrolled()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            var item = await conn.QueryFirstOrDefaultAsync<GovItem>(_querySql);
            if (item == null)
            {
                MarkInvalid(ScenarioUncontrolled, FrameworkDapper, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioUncontrolled, FrameworkDapper, $"Exception: {ex.GetType().Name}"));
    }

    // ── Dapper + StormGate ────────────────────────────────────────────────────
    // StormGate limits to 20 concurrent connection opens — below the server's 25.
    // Remaining tasks wait in the semaphore queue.  Zero server-side rejections.
    // Expected result: succeeds with measured throughput.

    [Benchmark(Baseline = true)]
    public async Task Dapper_StormGate()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            var stopwatch = Stopwatch.StartNew();
            await using var conn = await _stormGate.OpenAsync();
            var item = await conn.QueryFirstOrDefaultAsync<GovItem>(_querySql);
            stopwatch.Stop();
            _stormGateLatencyTicks.Add(stopwatch.ElapsedTicks);
            if (item == null)
            {
                MarkInvalid(ScenarioGoverned, FrameworkStormGate, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioGoverned, FrameworkStormGate, $"Exception: {ex.GetType().Name}"));
    }

    // ── Uncontrolled EF Core ──────────────────────────────────────────────────
    // Same pool behaviour as uncontrolled Dapper — EF Core's internal NpgsqlDataSource
    // pool defaults to max 100 connections.  Postgres still hard-caps at 25.
    // Expected result: NA (same 53300 crash).

    [Benchmark]
    public async Task EF_Uncontrolled()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new GovEfDbContext(_efOptions);
            var item = await ctx.GovItems.AsNoTracking().FirstOrDefaultAsync();
            if (item == null)
            {
                MarkInvalid(ScenarioUncontrolled, FrameworkEntityFramework, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioUncontrolled, FrameworkEntityFramework, $"Exception: {ex.GetType().Name}"));
    }

    // ── Setup helpers ─────────────────────────────────────────────────────────

    private async Task WaitForReadyAsync()
    {
        for (var i = 0; i < 60; i++)
        {
            try
            {
                await using var conn = await _dapperDataSource.OpenConnectionAsync();
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        throw new TimeoutException("PostgreSQL container did not become ready in time.");
    }

    private async Task SeedAsync()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS gov_items (
                id  SERIAL PRIMARY KEY,
                val INTEGER NOT NULL
            );
            INSERT INTO gov_items (val) VALUES (42);
            """);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        BenchmarkCorrectnessArtifacts.Write(nameof(PostgreSqlConnectionGovernanceBenchmarks),
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
        WriteLatencySidecar();

        _stormGate.Dispose();
        await _dapperDataSource.DisposeAsync();

        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync() => await GlobalCleanup();

    private void MarkInvalid(string scenario, string framework, string reason)
    {
        var key = new CorrectnessIssueKey("*", scenario, framework, reason);
        _correctnessIssues.AddOrUpdate(key, 1, static (_, current) => current + 1);
    }

    private void WriteLatencySidecar()
    {
        var ticks = _stormGateLatencyTicks.ToArray();
        Array.Sort(ticks);

        static double TicksToMs(long t) => (double)t / Stopwatch.Frequency * 1000.0;

        long Percentile(long[] sorted, double pct)
        {
            if (sorted.Length == 0)
            {
                return 0;
            }

            var idx = (int)Math.Ceiling(pct / 100.0 * sorted.Length) - 1;
            return sorted[Math.Max(0, Math.Min(idx, sorted.Length - 1))];
        }

        var stormGateFailureCount = _correctnessIssues
            .Where(kvp => kvp.Key.Framework == FrameworkStormGate)
            .Sum(kvp => kvp.Value);
        var dapperFailureCount = _correctnessIssues
            .Where(kvp => kvp.Key.Framework == FrameworkDapper)
            .Sum(kvp => kvp.Value);
        var efFailureCount = _correctnessIssues
            .Where(kvp => kvp.Key.Framework == FrameworkEntityFramework)
            .Sum(kvp => kvp.Value);

        var sb = new StringBuilder();
        sb.AppendLine("# PostgreSqlConnectionGovernanceBenchmarks — Governed Latency");
        sb.AppendLine();
        sb.AppendLine($"PostgreSQL max_connections: {PgMaxConnections}");
        sb.AppendLine($"StormGate permits: {StormGatePermits}");
        sb.AppendLine($"Parallelism: {Parallelism}");
        sb.AppendLine($"Operations per run: {OperationsPerRun}");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| P50 | {TicksToMs(Percentile(ticks, 50)):F3} ms |");
        sb.AppendLine($"| P95 | {TicksToMs(Percentile(ticks, 95)):F3} ms |");
        sb.AppendLine($"| P99 | {TicksToMs(Percentile(ticks, 99)):F3} ms |");
        sb.AppendLine($"| Max | {(ticks.Length == 0 ? 0 : TicksToMs(ticks[^1])):F3} ms |");
        sb.AppendLine();
        sb.AppendLine($"StormGate failure count: {stormGateFailureCount}");
        sb.AppendLine($"Dapper uncontrolled failure count: {dapperFailureCount}");
        sb.AppendLine($"EF uncontrolled failure count: {efFailureCount}");

        try
        {
            var dir = Path.Combine("BenchmarkDotNet.Artifacts", "results");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{nameof(PostgreSqlConnectionGovernanceBenchmarks)}-latency.md");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"[PostgreSqlConnectionGovernanceBenchmarks] Wrote {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PostgreSqlConnectionGovernanceBenchmarks] Failed to write latency sidecar: {ex.Message}");
        }
    }

    // ── Nested types ──────────────────────────────────────────────────────────

    private class GovItem
    {
        public int Id { get; set; }
        public int Val { get; set; }
    }

    private class GovEfDbContext : DbContext
    {
        public GovEfDbContext(DbContextOptions<GovEfDbContext> options) : base(options) { }

        public DbSet<GovEfItem> GovItems { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<GovEfItem>(e =>
            {
                e.ToTable("gov_items");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.Val).HasColumnName("val");
            });
        }
    }

    private class GovEfItem
    {
        public int Id { get; set; }
        public int Val { get; set; }
    }
}
