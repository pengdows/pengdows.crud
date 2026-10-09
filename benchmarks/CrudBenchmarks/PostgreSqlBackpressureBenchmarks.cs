using System.Data;
using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.stormgate;

namespace CrudBenchmarks;

/// <summary>
/// OVERLOAD, NOT A FAILURE COUNT: does admission control turn overload into backpressure?
///
/// A pool capped at the right size keeps the connection count down; it does not say no. This offers
/// calls at a fixed rate above capacity (open loop: arrivals continue whether or not earlier calls
/// have finished, which the earlier closed-loop storms could not do) and records what each way of
/// bounding concurrency does with the excess: how long a refused caller waits to hear no, how long the
/// calls that were admitted take, how large the backlog grows, what a small unrelated query sharing the
/// same path experiences, and how long the system takes to settle once the load stops.
///
/// Every arm gets the same concurrency (20), the same service time (300 ms, so capacity is 20/0.3 =
/// 66.7 calls/s), the same offered load (2x capacity, 133/s for 20 s) and the same wait budget (5 s where
/// it has one). The server allows 25 connections, so nothing here can fail by server rejection; any
/// difference is the admission control.
///
/// EXPECTATIONS, written before any result existed — a result that contradicts one is a finding, not
/// something to explain away:
///   1. Goodput is the same for every arm (about 66/s): bounding concurrency cannot create capacity.
///   2. The pool alone at its default Timeout (15 s): admitted-call latency grows toward the timeout and
///      most failures arrive at about 15 s, together. A backlog the size of the whole overload builds.
///   3. The pool alone with Timeout tuned to the same 5 s, StormGate with 5 s, and the governor with 5 s
///      behave about the same as each other: a wait budget is a wait budget. This is the honest baseline;
///      if these differ materially the explanation is something other than the budget.
///   4. Only a BOUNDED QUEUE refuses early: refusals arrive in milliseconds, admitted-call latency stays
///      near service time plus queue depth / capacity, and the backlog never exceeds the bound.
///   5. The unrelated probe is delayed by the same queue as the work in every arm without a bound, and
///      much less with one.
/// If 3 holds and 4 does not, the value is smaller than believed and the report should say so.
/// </summary>
[OptInBenchmark]
[SimpleJob(warmupCount: 0, iterationCount: 1, invocationCount: 1)]
public class PostgreSqlBackpressureBenchmarks : IAsyncDisposable
{
    internal const int ServerMaxConnections = 25;
    internal const int Concurrency = 20;
    internal const int ServiceMilliseconds = 300;
    internal const double OverloadFactor = 2.0;
    internal const double LoadSeconds = 20;
    internal const double SettleSeconds = 60;
    internal const double ProbePerSecond = 5;
    internal const int BoundedQueueDepth = 40;
    internal const int TunedPoolTimeoutSeconds = 5;

    internal static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(5);

    internal static double CapacityPerSecond => Concurrency * 1000.0 / ServiceMilliseconds;
    internal static double OfferedRatePerSecond => CapacityPerSecond * OverloadFactor;

    private const string Password = "backpressure_pw";
    private const string WorkSql = "SELECT 1 FROM pg_sleep(0.3)";
    private const string ProbeSql = "SELECT 1";

    private IContainer? _container;
    private string _baseConnectionString = string.Empty;
    private string _monitorConnectionString = string.Empty;

    private NpgsqlDataSource? _dataSource;
    private StormGate? _stormGate;
    private DatabaseContext? _context;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", Password)
            .WithEnvironment("POSTGRES_DB", "bp")
            .WithPortBinding(0, 5432)
            .WithCommand("-c", $"max_connections={ServerMaxConnections}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();
        await _container.StartAsync();

        var port = _container.GetMappedPublicPort(5432);
        _baseConnectionString = $"Host=localhost;Port={port};Database=bp;Username=postgres;Password={Password}";
        _monitorConnectionString = _baseConnectionString + ";Pooling=false";

        for (var i = 0; i < 60; i++)
        {
            try
            {
                await using var probe = new NpgsqlConnection(_monitorConnectionString);
                await probe.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        throw new TimeoutException("PostgreSQL container did not become ready.");
    }

    // ── arms ───────────────────────────────────────────────────────────────────

    // The provider pool alone, correctly sized, everything else at its default (Timeout 15 s).
    [Benchmark]
    public Task PoolOnly_Dapper() =>
        RunDapperPoolArm("PoolOnly_Dapper", $";Maximum Pool Size={Concurrency}");

    // The same pool with Npgsql's Timeout tuned to the shared 5 s wait budget: the strongest plain baseline.
    [Benchmark]
    public Task PoolOnly_Dapper_TunedTimeout() =>
        RunDapperPoolArm("PoolOnly_Dapper_TunedTimeout",
            $";Maximum Pool Size={Concurrency};Timeout={TunedPoolTimeoutSeconds}");

    // StormGate: 20 permits and a 5 s wait in front of a default-sized pool.
    [Benchmark]
    public Task StormGate_Dapper()
    {
        _dataSource = NpgsqlDataSource.Create(_baseConnectionString);
        _stormGate = new StormGate(_dataSource, Concurrency, AcquireTimeout);
        return RunScenarioAsync("StormGate_Dapper",
            async ct =>
            {
                await using var conn = await _stormGate.OpenAsync(ct);
                await conn.ExecuteScalarAsync<int>(WorkSql);
            },
            async ct =>
            {
                await using var conn = await _stormGate.OpenAsync(ct);
                await conn.ExecuteScalarAsync<int>(ProbeSql);
            });
    }

    // pengdows.crud's PoolGovernor: 20 slots, 5 s wait, no queue bound (the 2.0.x default).
    [Benchmark]
    public Task Pengdows_Governor() => RunPengdowsArm("Pengdows_Governor", maxQueuedReads: null);

    // The same governor with a bounded wait queue: callers beyond it are refused at once.
    [Benchmark]
    public Task Pengdows_Governor_BoundedQueue() =>
        RunPengdowsArm("Pengdows_Governor_BoundedQueue", maxQueuedReads: BoundedQueueDepth);

    private Task RunDapperPoolArm(string arm, string poolSettings)
    {
        _dataSource = NpgsqlDataSource.Create(_baseConnectionString + poolSettings);
        return RunScenarioAsync(arm,
            async ct =>
            {
                await using var conn = await _dataSource.OpenConnectionAsync(ct);
                await conn.ExecuteScalarAsync<int>(WorkSql);
            },
            async ct =>
            {
                await using var conn = await _dataSource.OpenConnectionAsync(ct);
                await conn.ExecuteScalarAsync<int>(ProbeSql);
            });
    }

    private Task RunPengdowsArm(string arm, int? maxQueuedReads)
    {
        _context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = _baseConnectionString,
            ProviderName = "Npgsql",
            DbMode = DbMode.Standard,
            MaxConcurrentReads = Concurrency,
            MaxConcurrentWrites = Concurrency,
            PoolAcquireTimeout = AcquireTimeout,
            MaxQueuedReads = maxQueuedReads,
            EnableMetrics = true
        }, NpgsqlFactory.Instance);

        return RunScenarioAsync(arm,
            async ct =>
            {
                await using var sc = _context.CreateSqlContainer(WorkSql);
                await sc.ExecuteScalarOrNullAsync<int>(ExecutionType.Read, CommandType.Text, ct);
            },
            async ct =>
            {
                await using var sc = _context.CreateSqlContainer(ProbeSql);
                await sc.ExecuteScalarOrNullAsync<int>(ExecutionType.Read, CommandType.Text, ct);
            });
    }

    // ── scenario ───────────────────────────────────────────────────────────────

    // BenchmarkDotNet invokes a benchmark method more than once (a jitting call, then the measured one).
    // Each arm builds its own pool, gate or context, so each call must release it: a pool left open by
    // the first call holds server connections and turns the second call into a different experiment.
    private async Task ReleaseArmResourcesAsync()
    {
        _context?.Dispose();
        _context = null;
        _stormGate?.Dispose();
        _stormGate = null;
        if (_dataSource != null)
        {
            await _dataSource.DisposeAsync();
            _dataSource = null;
        }

        NpgsqlConnection.ClearAllPools();
    }

    private async Task RunScenarioAsync(string arm, Func<CancellationToken, Task> work, Func<CancellationToken, Task> probe)
    {
        try
        {
            await RunScenarioCoreAsync(arm, work, probe);
        }
        finally
        {
            await ReleaseArmResourcesAsync();
        }
    }

    private async Task RunScenarioCoreAsync(string arm, Func<CancellationToken, Task> work, Func<CancellationToken, Task> probe)
    {
        GC.Collect();
        var baselineMemory = GC.GetTotalMemory(true);

        int peakThreads = 0;
        int peakServerConnections = 0;
        long peakPending = 0;
        long peakMemory = 0;
        using var sampling = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            await using var monitor = new NpgsqlConnection(_monitorConnectionString);
            await monitor.OpenAsync();
            while (!sampling.IsCancellationRequested)
            {
                peakThreads = Math.Max(peakThreads, ThreadPool.ThreadCount);
                peakPending = Math.Max(peakPending, ThreadPool.PendingWorkItemCount);
                peakMemory = Math.Max(peakMemory, GC.GetTotalMemory(false));
                try
                {
                    await using var cmd = monitor.CreateCommand();
                    cmd.CommandText =
                        "SELECT count(*) FROM pg_stat_activity WHERE backend_type = 'client backend' AND pid <> pg_backend_pid()";
                    peakServerConnections = Math.Max(peakServerConnections, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
                }
                catch
                {
                    // the monitor can itself be refused while the server is full
                }

                await Task.Delay(100);
            }
        });

        var records = await OpenLoopLoad.RunAsync(
            OfferedRatePerSecond, LoadSeconds, work, ProbePerSecond, probe, SettleSeconds);

        sampling.Cancel();
        await sampler;

        var s = BackpressureStats.Summarize(records, loadEndMs: LoadSeconds * 1000, fastRejectMs: 1000);
        var errors = string.Join(",", s.Errors.OrderByDescending(e => e.Value).Select(e => $"{e.Key}:{e.Value}"));
        var line =
            $"arm={arm} offered={s.Offered} ok={s.Ok} fastReject={s.FastRejects} slowFail={s.SlowFailures} " +
            $"okP50ms={s.OkP50Ms:F0} okP99ms={s.OkP99Ms:F0} okMaxMs={s.OkMaxMs:F0} " +
            $"failP50ms={s.FailP50Ms:F0} failP99ms={s.FailP99Ms:F0} peakInFlight={s.PeakInFlight} " +
            $"recoveryMs={s.RecoveryMs:F0} probeOk={s.ProbeOk} probeFail={s.ProbeFailed} " +
            $"probeP50ms={s.ProbeP50Ms:F0} probeP99ms={s.ProbeP99Ms:F0} peakThreads={peakThreads} " +
            $"peakPendingWork={peakPending} peakMemMB={(peakMemory - baselineMemory) / 1048576.0:F0} " +
            $"peakServerConns={peakServerConnections} errors={errors}";
        Console.WriteLine("[BACKPRESSURE] " + line);
        WriteSidecar(arm, line, s);
    }

    private static void WriteSidecar(string arm, string line, BackpressureSummary s)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# PostgreSqlBackpressureBenchmarks — {arm}");
            sb.AppendLine();
            sb.AppendLine($"Offered {OfferedRatePerSecond:F0}/s for {LoadSeconds:F0}s against capacity " +
                          $"{CapacityPerSecond:F1}/s ({Concurrency} concurrent x {ServiceMilliseconds} ms); " +
                          $"server max_connections={ServerMaxConnections}; wait budget {AcquireTimeout.TotalSeconds:F0}s.");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(line);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine($"Goodput (ok/offered window): {s.Ok / LoadSeconds:F1}/s");

            var dir = Environment.GetEnvironmentVariable("CRUD_BENCH_ARTIFACTS_DIR")
                      ?? Path.Combine("BenchmarkDotNet.Artifacts", "results");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"PostgreSqlBackpressureBenchmarks-{arm}-backpressure.md"), sb.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PostgreSqlBackpressureBenchmarks] Failed to write sidecar: {ex.Message}");
        }
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _context?.Dispose();
        _stormGate?.Dispose();
        if (_dataSource != null)
        {
            await _dataSource.DisposeAsync();
        }

        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync() => await GlobalCleanup();
}
