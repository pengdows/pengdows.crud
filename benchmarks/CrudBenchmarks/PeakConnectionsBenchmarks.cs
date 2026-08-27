using System.Collections.Concurrent;
using System.Data;
using System.Text;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using pengdows.crud;
using pengdows.crud.attributes;

namespace CrudBenchmarks;

/// <summary>
/// Answers a question none of this suite's other benchmarks measure directly: at a FIXED,
/// matched request rate — not matched concurrency — how many live server-side connections
/// does each framework actually hold to sustain that rate?
///
/// Matched concurrency (e.g. "100 concurrent callers for everyone") is not a fair comparison
/// here: by Little's Law (L = λW), a framework with a longer per-operation connection-hold
/// time (W) needs more work-in-progress (L) — and therefore more held connections — to
/// sustain the same completion rate (λ) as a framework with a shorter hold time. This
/// benchmark drives all three frameworks at the SAME target operation rate via a
/// PeriodicTimer-based dispatcher (new logical operations are submitted on a fixed schedule,
/// independent of how quickly prior ones complete), and a background sampler polls
/// `pg_stat_activity` every 10ms for the run's duration to report peak and mean non-idle
/// connection count. The actually-achieved throughput is reported alongside the target rate
/// so a reader can confirm the target was genuinely sustained, not silently missed (a
/// framework that can't keep up would show completed ops/sec well below the target, which
/// would invalidate the "matched throughput" comparison for that framework's row).
///
/// This is the direct, empirical form of the open-late/close-early argument this project
/// makes elsewhere as an assertion — see docs/positioning/product-thesis.md principle 5.
/// </summary>
[OptInBenchmark]
[SimpleJob(warmupCount: 0, iterationCount: 1, invocationCount: 1)]
public class PeakConnectionsBenchmarks : IAsyncDisposable
{
    private const int TargetOpsPerSecond = 200;
    private const int RunDurationSeconds = 8;
    private const int SampleIntervalMs = 10;
    private const int SeedRowCount = 1000;

    private IContainer? _container;
    private string _connStr = null!;

    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<PeakEntity, int> _pengdowsGateway = null!;
    private NpgsqlDataSource _dapperDataSource = null!;
    private DbContextOptions<PeakEfDbContext> _efOptions = null!;

    private readonly ConcurrentDictionary<CorrectnessIssueKey, int> _correctnessIssues = new();

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var password = Guid.NewGuid().ToString("N");
        _container = new ContainerBuilder()
            .WithImage("postgres:16-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", password)
            .WithEnvironment("POSTGRES_DB", "peak_test")
            .WithPortBinding(0, 5432)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _container.StartAsync();

        var port = _container.GetMappedPublicPort(5432);
        _connStr = $"Host=localhost;Port={port};Database=peak_test;Username=postgres;Password={password}";

        await WaitForReadyAsync();
        await SeedAsync();

        _pengdowsContext = new DatabaseContext(_connStr, NpgsqlFactory.Instance);
        _pengdowsGateway = new TableGateway<PeakEntity, int>(_pengdowsContext);

        _dapperDataSource = NpgsqlDataSource.Create(_connStr);

        _efOptions = new DbContextOptionsBuilder<PeakEfDbContext>().UseNpgsql(_connStr).Options;

        Console.WriteLine(
            $"[PeakConnections] target={TargetOpsPerSecond}/s, duration={RunDurationSeconds}s, " +
            $"sample interval={SampleIntervalMs}ms");
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        BenchmarkCorrectnessArtifacts.Write(nameof(PeakConnectionsBenchmarks),
            _correctnessIssues
                .Select(pair => new CorrectnessIssue(
                    pair.Key.ParameterKey == "*" ? null : pair.Key.ParameterKey,
                    pair.Key.Scenario, pair.Key.Framework, pair.Key.Reason, pair.Value))
                .ToArray());

        _pengdowsContext?.Dispose();
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    // ── Shared driver ────────────────────────────────────────────────────────

    private async Task<RunResult> RunMatchedRateAsync(string framework, Func<Task> operation)
    {
        var completed = 0L;
        var failed = 0L;
        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(RunDurationSeconds));

        var samples = new ConcurrentBag<int>();
        using var samplerCts = CancellationTokenSource.CreateLinkedTokenSource(runCts.Token);
        var samplerTask = Task.Run(async () =>
        {
            while (!samplerCts.IsCancellationRequested)
            {
                try
                {
                    var count = await SampleActiveConnectionsAsync();
                    samples.Add(count);
                    await Task.Delay(SampleIntervalMs, samplerCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PeakConnections] sampler error: {ex.Message}");
                }
            }
        });

        var inFlight = new ConcurrentBag<Task>();
        var interval = TimeSpan.FromMilliseconds(1000.0 / TargetOpsPerSecond);
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(runCts.Token))
            {
                var t = Task.Run(async () =>
                {
                    try
                    {
                        await operation();
                        Interlocked.Increment(ref completed);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        MarkInvalid(framework, $"Exception: {ex.GetType().Name}");
                    }
                });
                inFlight.Add(t);
            }
        }
        catch (OperationCanceledException)
        {
            // expected: runCts fired
        }

        // Drain whatever was still in flight when the timer stopped, with a bounded grace
        // period so a genuinely stuck framework doesn't hang the whole benchmark run.
        var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await Task.WhenAll(inFlight.ToArray()).WaitAsync(drainCts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[PeakConnections] {framework}: drain timed out with tasks still in flight");
        }

        samplerCts.Cancel();
        try { await samplerTask; } catch { /* already logged above */ }

        var sampleArray = samples.ToArray();
        var peak = sampleArray.Length > 0 ? sampleArray.Max() : 0;
        var mean = sampleArray.Length > 0 ? sampleArray.Average() : 0.0;
        var achievedOpsPerSec = completed / (double)RunDurationSeconds;

        var result = new RunResult(framework, completed, failed, peak, mean, achievedOpsPerSec, sampleArray.Length);
        WriteResultSidecar(result);
        return result;
    }

    private async Task<int> SampleActiveConnectionsAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        // Exclude this sampler's own connection (pid != pg_backend_pid()) so the act of
        // sampling doesn't add 1 to every reading it takes.
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() " +
            "AND state <> 'idle' AND pid != pg_backend_pid()");
    }

    // ── Benchmarks ───────────────────────────────────────────────────────────

    [Benchmark]
    public async Task Pengdows()
    {
        await RunMatchedRateAsync(FrameworkPengdows, async () =>
        {
            var id = Random.Shared.Next(1, SeedRowCount + 1);
            var entity = await _pengdowsGateway.RetrieveOneAsync(id);
            if (entity == null)
            {
                MarkInvalid(FrameworkPengdows, "Expected row not found");
            }
        });
    }

    [Benchmark]
    public async Task Dapper()
    {
        await RunMatchedRateAsync(FrameworkDapper, async () =>
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            var id = Random.Shared.Next(1, SeedRowCount + 1);
            var entity = await conn.QueryFirstOrDefaultAsync<PeakEntity>(
                "SELECT id, val FROM peak_items WHERE id = @id", new { id });
            if (entity == null)
            {
                MarkInvalid(FrameworkDapper, "Expected row not found");
            }
        });
    }

    [Benchmark]
    public async Task EntityFramework()
    {
        await RunMatchedRateAsync(FrameworkEntityFramework, async () =>
        {
            await using var ctx = new PeakEfDbContext(_efOptions);
            var id = Random.Shared.Next(1, SeedRowCount + 1);
            var entity = await ctx.PeakItems.FindAsync(id);
            if (entity == null)
            {
                MarkInvalid(FrameworkEntityFramework, "Expected row not found");
            }
        });
    }

    // ── Setup helpers ────────────────────────────────────────────────────────

    private async Task WaitForReadyAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connStr);
                await conn.OpenAsync();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(500);
            }
        }
        throw new TimeoutException("Postgres not ready in time.", last);
    }

    private async Task SeedAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS peak_items (id INT PRIMARY KEY, val INT NOT NULL)");
        for (var i = 1; i <= SeedRowCount; i++)
        {
            await conn.ExecuteAsync(
                "INSERT INTO peak_items (id, val) VALUES (@id, @val) ON CONFLICT (id) DO NOTHING",
                new { id = i, val = i * 7 });
        }
    }

    // ── Correctness + result reporting ──────────────────────────────────────

    private const string FrameworkPengdows = "Pengdows";
    private const string FrameworkDapper = "Dapper";
    private const string FrameworkEntityFramework = "EntityFramework";

    private void MarkInvalid(string framework, string reason)
    {
        var key = new CorrectnessIssueKey("*", "MatchedRateRead", framework, reason);
        _correctnessIssues.AddOrUpdate(key, 1, static (_, count) => count + 1);
    }

    private readonly record struct RunResult(
        string Framework, long Completed, long Failed, int PeakConnections,
        double MeanConnections, double AchievedOpsPerSec, int SampleCount);

    private void WriteResultSidecar(RunResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## PeakConnectionsBenchmarks — {r.Framework}");
        sb.AppendLine();
        sb.AppendLine($"Target rate: {TargetOpsPerSecond} ops/sec for {RunDurationSeconds}s");
        sb.AppendLine($"Achieved rate: {r.AchievedOpsPerSec:0.0} ops/sec ({r.Completed} completed, {r.Failed} failed)");
        if (r.AchievedOpsPerSec < TargetOpsPerSecond * 0.9)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"**WARNING: achieved rate is more than 10% below target — {r.Framework} could not " +
                "keep up with the dispatch schedule. Peak/mean connection numbers below reflect an " +
                "unbounded-queue overload state for this framework, not a genuinely matched-throughput " +
                "comparison. Do not compare this row's connection counts against a framework that DID " +
                "sustain the target rate.**");
        }
        sb.AppendLine();
        sb.AppendLine($"Peak concurrent server connections (non-idle): **{r.PeakConnections}**");
        sb.AppendLine($"Mean concurrent server connections (non-idle): {r.MeanConnections:0.00}");
        sb.AppendLine($"Sampler observations: {r.SampleCount}");
        sb.AppendLine();

        try
        {
            var dir = Environment.GetEnvironmentVariable("CRUD_BENCH_ARTIFACTS_DIR")
                ?? Path.Combine("BenchmarkDotNet.Artifacts", "results");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{nameof(PeakConnectionsBenchmarks)}-{r.Framework}-result.md");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"[PeakConnections] Wrote {path}");
            Console.Write(sb.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PeakConnections] Failed to write result sidecar: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _pengdowsContext?.Dispose();
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }

    // ── Entities ─────────────────────────────────────────────────────────────

    [Table("peak_items")]
    public class PeakEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("val", DbType.Int32)]
        public int Val { get; set; }
    }

    public class PeakEfDbContext : DbContext
    {
        public PeakEfDbContext(DbContextOptions<PeakEfDbContext> options) : base(options) { }

        public DbSet<PeakEntity> PeakItems { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<PeakEntity>(e =>
            {
                e.ToTable("peak_items");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.Val).HasColumnName("val");
            });
        }
    }
}
