using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.stormgate;

namespace CrudBenchmarks;

/// <summary>
/// THESIS PROOF: PostgreSQL connection governance under overload.
///
/// Demonstrates what happens when application concurrency exceeds the database
/// server's max_connections limit — and how StormGate prevents the crash.
///
/// The postgres container is started with max_connections=25.
/// All benchmarks run StormOperationsPerRun operations at StormParallelism-way parallelism (40× the
/// server limit as of 2026-10-08; it was 200 operations at 100-way, 4×, in the runs quoted below),
/// repeated across (WarmupCount=1 + IterationCount=3) × InvocationCount=5 = 20 invocations per
/// method.
///
/// NOTE (2026-10-08): the measurements in this comment were taken when every client's Npgsql
/// pool max was the default 100. Since then all clients share a connection string with pool
/// is back to the default (100) and the server limit is 25, so "Npgsql pool (max 100)" below describes that earlier run,
/// not the current configuration; re-measure before quoting any figure here.
///
/// Without a connection governor, Dapper and EF Core fail for TWO DIFFERENT REASONS —
/// confirmed against the raw correctness-fragment JSON on 2026-08-27, not assumed:
///   - Dapper: the Npgsql pool (max 100) attempts to open 100 physical connections.
///     PostgreSQL itself rejects any beyond 25 with SQLSTATE 53300 "sorry, too many
///     clients already" — a hard, server-side PostgresException. Measured: 1,950/4,000
///     (48.75%) failed this way. This is not tunable from the client; it's the server's
///     own admission limit being hit directly.
///   - EF Core: fails earlier and differently — its own Npgsql pool throws a CLIENT-side
///     InvalidOperationException (pool timeout waiting for an available connection) before
///     most attempts even reach the server. Measured: 2,158/4,000 (53.95%) failed this way.
///     This IS tunable (Npgsql's pool `Timeout`/`Connection Idle Lifetime`), so it is a
///     softer, configuration-dependent failure mode, not proof of the same server rejection
///     Dapper hits. Do not describe these as "the same crash."
///
/// With StormGate (20 permits, well below the server's 25-connection limit):
///   - At most 20 concurrent connection opens ever reach the server.
///   - The remaining 80 concurrent tasks wait in the semaphore queue.
///   - Measured: 0/4,000 failures for Dapper_StormGate.
///
/// Pengdows_Governed runs the same load through a DatabaseContext/TableGateway on the shared
/// default pool (no explicit ceiling; pengdows.crud sizes its PoolGovernor from the connection
/// string or config, and here it gets neither), with no StormGate. Added 2026-10-08; no measured result is recorded here, so
/// read the correctness fragments from the run instead of trusting this comment.
///
/// The headline number is the failure count going to zero (1,950 → 0, 2,158 → 0), not a
/// derived "Nx faster" ratio — Dapper_Uncontrolled's raw mean latency is inflated by the
/// cost of throwing/catching a PostgresException on roughly half of all attempts, so a
/// mean-latency ratio against it overstates StormGate's real per-query speedup.
/// </summary>
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3, invocationCount: 5)]
public class PostgreSqlConnectionGovernanceBenchmarks : IAsyncDisposable
{
    private const string FrameworkDapper = "Dapper";
    private const string FrameworkStormGate = "StormGate";
    private const string FrameworkEntityFramework = "EntityFramework";
    private const string FrameworkPengdows = "Pengdows";
    private const string FrameworkPengdowsClamped = "PengdowsClamped";
    private const string FrameworkPengdowsClampedPatient = "PengdowsClampedPatient";
    private const string ScenarioUncontrolled = "Uncontrolled";
    private const string ScenarioGoverned = "Governed";
    private const string ScenarioGovernedEf = "GovernedEf";
    internal const int ServerMaxConnections = 25;   // server limit
    private const int PgMaxConnections = ServerMaxConnections;
    internal static readonly TimeSpan StormGateAcquireTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan PatientPoolAcquireTimeout = StormGateAcquireTimeout;
    private const int StormGatePermits = 20;   // well under server limit; tasks queue, not crash
    internal const int StormParallelism = 1000;     // 40x the server limit
    private const int Parallelism = StormParallelism;
    internal const int StormOperationsPerRun = 5000;

    // Calls that hold the connection for real work. 0 keeps every earlier instant-query row comparable;
    // SlowWorkMilliseconds is a spike of slow calls. 1,500 callers x 300 ms through ~20 concurrent
    // connections drains in ~22 s: past Npgsql's default 15 s pool timeout, so a pool that merely
    // queues fails its tail, and a gate that waits long enough does not.
    internal const int SlowWorkMilliseconds = 300;
    private const int SlowOperationsPerRun = 1500;

    [Params(0, SlowWorkMilliseconds)]
    public int WorkMilliseconds { get; set; }

    internal static int OperationsFor(int workMilliseconds) =>
        workMilliseconds == 0 ? StormOperationsPerRun : SlowOperationsPerRun;

    // Must equal the text BenchmarkDotNet puts between '[' and ']' in the case's display info:
    // CorrectnessColumn derives its lookup key from there.
    internal static string ParameterKeyFor(int workMilliseconds) => $"WorkMilliseconds={workMilliseconds}";

    private int OperationsPerRun => OperationsFor(WorkMilliseconds);

    // Holds the connection for the work duration inside the one query, so every arm (raw SQL, EF and
    // the gateway alike) keeps its lease for the same time.
    internal static string QuerySqlFor(int workMilliseconds) =>
        workMilliseconds == 0
            ? "SELECT id, val FROM gov_items WHERE id = 1"
            : "SELECT id, val FROM gov_items, LATERAL (SELECT pg_sleep(" +
              (workMilliseconds / 1000.0).ToString(System.Globalization.CultureInfo.InvariantCulture) +
              ")) AS work WHERE id = 1";

    private static readonly string PgPassword = GeneratePassword();

    private static string GeneratePassword()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(18);
        return Convert.ToBase64String(bytes);
    }

    private IContainer? _container;
    private string _connStr = string.Empty;

    private NpgsqlDataSource _dapperDataSource = null!;  // default pool (100) — above the server limit
    private StormGate _stormGate = null!;                // 20 permits — stays under server limit
    private DbContextOptions<GovEfDbContext> _efOptions = null!;
    private DatabaseContext _pengdowsContext = null!;    // PoolGovernor only — NO StormGate
    private TableGateway<GovPgEntity, int> _pengdowsGateway = null!;
    private DatabaseContext _pengdowsClampedContext = null!;   // same, with ClampPoolsToServerConnectionLimit on
    private TableGateway<GovPgEntity, int> _pengdowsClampedGateway = null!;
    private DatabaseContext _pengdowsPatientContext = null!;   // clamp on, and StormGate's 30 s wait for a slot
    private TableGateway<GovPgEntity, int> _pengdowsPatientGateway = null!;

    private string _querySql = null!;
    private readonly ConcurrentDictionary<CorrectnessIssueKey, int> _correctnessIssues = new();
    private readonly ConcurrentBag<long> _stormGateLatencyTicks = new();
    private readonly ConcurrentBag<long> _efStormGateLatencyTicks = new();
    private readonly ConcurrentBag<long> _pengdowsLatencyTicks = new();

    // Total attempted operations across every invocation/iteration in THIS process (one
    // process per [Benchmark] method). Written into the correctness fragment's metadata so
    // the denominator behind a failure count is a recorded fact, not something inferred from
    // BenchmarkDotNet job config after the fact (that inference was needed once already —
    // see benchmarks/CrudBenchmarks/results — and shouldn't be needed again).
    private long _attempted;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", PgPassword)
            .WithEnvironment("POSTGRES_DB", "gov_test")
            .WithPortBinding(0, 5432)
            // Key to the demo: max_connections=25, with 1,000-way application concurrency.
            // No client's pool is capped: each runs on the Npgsql default (100), above the server's
            // 25. StormGate's 20 permits are the only admission control among the arms.
            .WithCommand("-c", $"max_connections={PgMaxConnections}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _container.StartAsync();

        var port = _container.GetMappedPublicPort(5432);
        _connStr = BuildClientConnectionString(
            $"Host=localhost;Port={port};Database=gov_test;Username=postgres;Password={PgPassword}");

        // Create the shared data source first so seeding uses the same pool as benchmarks.
        _dapperDataSource = NpgsqlDataSource.Create(_connStr);

        await WaitForReadyAsync();
        await SeedAsync();

        _stormGate = new StormGate(_dapperDataSource, StormGatePermits, StormGateAcquireTimeout);

        _efOptions = new DbContextOptionsBuilder<GovEfDbContext>()
            .UseNpgsql(_connStr)
            .Options;

        // Every client (Dapper, EF, pengdows.crud) shares _connStr with no pool ceiling set, so
        // pengdows.crud's PoolGovernor is sized from the dialect default (100), above the server's
        // 25, and it is NOT wrapped in StormGate. (Result of that configuration: see the
        // correctness fragments from the run; on 2026-10-08 it was 2,121 / 5,200 failures.)
        var typeMap = new TypeMapRegistry();
        typeMap.Register<GovPgEntity>();
        _pengdowsContext = new DatabaseContext(_connStr, NpgsqlFactory.Instance, typeMap);
        _pengdowsGateway = new TableGateway<GovPgEntity, int>(_pengdowsContext);

        // Identical connection string and load; the only difference is the opt-in clamp, which reads
        // the server's real limit (25, less the superuser reserve) and sizes the pools to it.
        _pengdowsClampedContext = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = _connStr,
                ProviderName = "Npgsql",
                DbMode = DbMode.Standard,
                ClampPoolsToServerConnectionLimit = true
            },
            NpgsqlFactory.Instance, null, typeMap);
        _pengdowsClampedGateway = new TableGateway<GovPgEntity, int>(_pengdowsClampedContext);

        _pengdowsPatientContext = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = _connStr,
                ProviderName = "Npgsql",
                DbMode = DbMode.Standard,
                ClampPoolsToServerConnectionLimit = true,
                PoolAcquireTimeout = PatientPoolAcquireTimeout
            },
            NpgsqlFactory.Instance, null, typeMap);
        _pengdowsPatientGateway = new TableGateway<GovPgEntity, int>(_pengdowsPatientContext);

        _querySql = QuerySqlFor(WorkMilliseconds);

        Console.WriteLine($"[GOV] postgres max_connections={PgMaxConnections}, " +
                          $"StormGate permits={StormGatePermits}, " +
                          $"test parallelism={Parallelism}");
    }

    // ── Uncontrolled Dapper ───────────────────────────────────────────────────
    // Npgsql pool max = default (100), above postgres max_connections = 25. Nothing queues in front of the pool.
    // Result: see the correctness fragments from the run, not this comment.

    [Benchmark]
    [CorrectnessIdentity(FrameworkDapper, ScenarioUncontrolled)]
    public async Task Dapper_Uncontrolled()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
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
    [CorrectnessIdentity(FrameworkStormGate, ScenarioGoverned)]
    public async Task Dapper_StormGate()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
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
    // Same default pool as uncontrolled Dapper (100) on EF Core's own Npgsql pool.
    // Result: see the correctness fragments from the run, not this comment.

    [Benchmark]
    [CorrectnessIdentity(FrameworkEntityFramework, ScenarioUncontrolled)]
    public async Task EF_Uncontrolled()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
            await using var ctx = new GovEfDbContext(_efOptions);
            var item = WorkMilliseconds == 0
                ? await ctx.GovItems.AsNoTracking().FirstOrDefaultAsync()
                : await ctx.GovItems.FromSqlRaw(_querySql).AsNoTracking().FirstOrDefaultAsync();
            if (item == null)
            {
                MarkInvalid(ScenarioUncontrolled, FrameworkEntityFramework, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioUncontrolled, FrameworkEntityFramework, $"Exception: {ex.GetType().Name}"));
    }

    // ── EF Core + StormGate ───────────────────────────────────────────────────
    // Completes the 2x2 matrix (Dapper/EF x Uncontrolled/Governed). Routes EF through a
    // StormGate-issued DbConnection via UseNpgsql(DbConnection) instead of letting EF manage
    // its own Npgsql pool, so the same 20-permit admission limit applies to EF as to Dapper.
    // Expected result: succeeds with measured throughput, same as Dapper_StormGate.

    [Benchmark]
    [CorrectnessIdentity(FrameworkStormGate, ScenarioGovernedEf)]
    public async Task EF_StormGate()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
            var stopwatch = Stopwatch.StartNew();
            await using var conn = await _stormGate.OpenAsync();
            var options = new DbContextOptionsBuilder<GovEfDbContext>()
                .UseNpgsql(conn)
                .Options;
            await using var ctx = new GovEfDbContext(options);
            var item = WorkMilliseconds == 0
                ? await ctx.GovItems.AsNoTracking().FirstOrDefaultAsync()
                : await ctx.GovItems.FromSqlRaw(_querySql).AsNoTracking().FirstOrDefaultAsync();
            stopwatch.Stop();
            _efStormGateLatencyTicks.Add(stopwatch.ElapsedTicks);
            if (item == null)
            {
                MarkInvalid(ScenarioGovernedEf, FrameworkStormGate, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioGovernedEf, FrameworkStormGate, $"Exception: {ex.GetType().Name}"));
    }

    // ── pengdows.crud (own PoolGovernor, no StormGate) ────────────────────────
    // Same load as the other arms; default pool, like the uncontrolled arms. The question this arm
    // answers: does pengdows.crud's built-in governance keep the failure count at zero where
    // ungoverned Dapper/EF hit 53300 / pool timeouts, without an external gate?

    [Benchmark]
    [CorrectnessIdentity(FrameworkPengdows, ScenarioGoverned)]
    public async Task Pengdows_Governed()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
            var stopwatch = Stopwatch.StartNew();
            await using var sc = WorkMilliseconds == 0
                ? _pengdowsGateway.BuildRetrieve(new[] { 1 })
                : _pengdowsContext.CreateSqlContainer(_querySql);
            var item = await _pengdowsGateway.LoadSingleAsync(sc);
            stopwatch.Stop();
            _pengdowsLatencyTicks.Add(stopwatch.ElapsedTicks);
            if (item == null)
            {
                MarkInvalid(ScenarioGoverned, FrameworkPengdows, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioGoverned, FrameworkPengdows, $"Exception: {ex.GetType().Name}"));
    }

    // No explicit pool ceiling for any client: every one runs on the provider default (Npgsql 100)
    // against a server capped at PgMaxConnections. Kept as one seam so the arms cannot diverge.
    // pengdows.crud with ClampPoolsToServerConnectionLimit on: same string, same load, no StormGate.
    [Benchmark]
    [CorrectnessIdentity(FrameworkPengdowsClamped, ScenarioGoverned)]
    public async Task Pengdows_Clamped()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
            await using var sc = WorkMilliseconds == 0
                ? _pengdowsClampedGateway.BuildRetrieve(new[] { 1 })
                : _pengdowsClampedContext.CreateSqlContainer(_querySql);
            var item = await _pengdowsClampedGateway.LoadSingleAsync(sc);
            if (item == null)
            {
                MarkInvalid(ScenarioGoverned, FrameworkPengdowsClamped, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioGoverned, FrameworkPengdowsClamped, $"Exception: {ex.GetType().Name}"));
    }

    // The clamped arm with the same wait-for-a-slot budget StormGate is given.
    [Benchmark]
    [CorrectnessIdentity(FrameworkPengdowsClampedPatient, ScenarioGoverned)]
    public async Task Pengdows_ClampedPatient()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
            await using var sc = WorkMilliseconds == 0
                ? _pengdowsPatientGateway.BuildRetrieve(new[] { 1 })
                : _pengdowsPatientContext.CreateSqlContainer(_querySql);
            var item = await _pengdowsPatientGateway.LoadSingleAsync(sc);
            if (item == null)
            {
                MarkInvalid(ScenarioGoverned, FrameworkPengdowsClampedPatient, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioGoverned, FrameworkPengdowsClampedPatient, $"Exception: {ex.GetType().Name}"));
    }

    internal static string BuildClientConnectionString(string connectionString) => connectionString;

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
                .ToArray(),
            Interlocked.Read(ref _attempted));
        Console.WriteLine($"[GOV] process {Environment.ProcessId} attempted {Interlocked.Read(ref _attempted)} operations");
        WriteLatencySidecar();

        _pengdowsPatientContext?.Dispose();
        _pengdowsClampedContext?.Dispose();
        _pengdowsContext?.Dispose();
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
        var key = new CorrectnessIssueKey(ParameterKeyFor(WorkMilliseconds), scenario, framework, reason);
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

        var efStormGateTicks = _efStormGateLatencyTicks.ToArray();
        Array.Sort(efStormGateTicks);

        // Each process runs exactly one [Benchmark] method, so _attempted and every entry in
        // _correctnessIssues here belong to that single method's own (scenario, framework) —
        // this is a per-process snapshot, not a merged view. The merged, cross-process view
        // lives in the correctness-fragments/*.json files (see BenchmarkCorrectnessArtifacts).
        var attempted = Interlocked.Read(ref _attempted);
        var failedThisProcess = _correctnessIssues.Sum(kvp => (long)kvp.Value);
        var failureRate = attempted == 0 ? 0.0 : (double)failedThisProcess / attempted * 100.0;

        var sb = new StringBuilder();
        sb.AppendLine("# PostgreSqlConnectionGovernanceBenchmarks — Governed Latency");
        sb.AppendLine();
        sb.AppendLine($"PostgreSQL max_connections: {PgMaxConnections}");
        sb.AppendLine($"StormGate permits: {StormGatePermits}");
        sb.AppendLine($"Parallelism: {Parallelism}");
        sb.AppendLine($"Operations per run: {OperationsPerRun}");
        sb.AppendLine();
        sb.AppendLine("## This process");
        sb.AppendLine();
        sb.AppendLine($"Attempted: {attempted}");
        sb.AppendLine($"Failed: {failedThisProcess} ({failureRate:F2}%)");
        sb.AppendLine();
        sb.AppendLine("Note: each [Benchmark] method runs in its own process (BenchmarkDotNet's "
            + "out-of-process toolchain), so this file only ever reflects ONE method per run. "
            + "For the merged, all-methods view, read correctness-fragments/*.json directly.");
        sb.AppendLine();
        sb.AppendLine("## Dapper_StormGate latency (this process, if it ran here)");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| P50 | {TicksToMs(Percentile(ticks, 50)):F3} ms |");
        sb.AppendLine($"| P95 | {TicksToMs(Percentile(ticks, 95)):F3} ms |");
        sb.AppendLine($"| P99 | {TicksToMs(Percentile(ticks, 99)):F3} ms |");
        sb.AppendLine($"| Max | {(ticks.Length == 0 ? 0 : TicksToMs(ticks[^1])):F3} ms |");
        sb.AppendLine();
        sb.AppendLine("## EF_StormGate latency (this process, if it ran here)");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| P50 | {TicksToMs(Percentile(efStormGateTicks, 50)):F3} ms |");
        sb.AppendLine($"| P95 | {TicksToMs(Percentile(efStormGateTicks, 95)):F3} ms |");
        sb.AppendLine($"| P99 | {TicksToMs(Percentile(efStormGateTicks, 99)):F3} ms |");
        sb.AppendLine($"| Max | {(efStormGateTicks.Length == 0 ? 0 : TicksToMs(efStormGateTicks[^1])):F3} ms |");

        var pengdowsTicks = _pengdowsLatencyTicks.ToArray();
        Array.Sort(pengdowsTicks);
        sb.AppendLine();
        sb.AppendLine("## Pengdows_Governed latency (this process, if it ran here)");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| P50 | {TicksToMs(Percentile(pengdowsTicks, 50)):F3} ms |");
        sb.AppendLine($"| P95 | {TicksToMs(Percentile(pengdowsTicks, 95)):F3} ms |");
        sb.AppendLine($"| P99 | {TicksToMs(Percentile(pengdowsTicks, 99)):F3} ms |");
        sb.AppendLine($"| Max | {(pengdowsTicks.Length == 0 ? 0 : TicksToMs(pengdowsTicks[^1])):F3} ms |");

        try
        {
            // Same durability fix as BenchmarkMetricsWriter/BenchmarkCorrectnessArtifacts: the
            // out-of-process toolchain's CWD is a generated directory BenchmarkDotNet deletes
            // during its own cleanup, so a relative path here is unrecoverable. This file was
            // confirmed missing entirely after the 2026-08-27 run for exactly this reason.
            var dir = Environment.GetEnvironmentVariable("CRUD_BENCH_ARTIFACTS_DIR")
                ?? Path.Combine("BenchmarkDotNet.Artifacts", "results");
            Directory.CreateDirectory(dir);
            // Per-process file name (like the correctness fragments) — a shared name would
            // have the same last-process-wins overwrite bug already fixed for correctness data.
            var path = Path.Combine(dir, $"{nameof(PostgreSqlConnectionGovernanceBenchmarks)}-{Environment.ProcessId}-latency.md");
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

    [Table("gov_items")]
    public class GovPgEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("val", DbType.Int32)]
        public int Val { get; set; }
    }

    private class GovEfItem
    {
        public int Id { get; set; }
        public int Val { get; set; }
    }
}
