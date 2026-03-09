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
    private const int PgMaxConnections = 25;   // deliberately low — below default Npgsql pool max
    private const int StormGatePermits = 20;   // well under server limit; tasks queue, not crash
    private const int Parallelism = 100;       // 4× the server limit — enough to saturate it
    private const int OperationsPerRun = 200;

    private IContainer? _container;
    private string _connStr = string.Empty;

    private NpgsqlDataSource _dapperDataSource = null!;  // pool max 100 — exceeds server limit
    private StormGate _stormGate = null!;                // 20 permits — stays under server limit
    private DbContextOptions<GovEfDbContext> _efOptions = null!;

    private string _querySql = null!;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
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
        _connStr = $"Host=localhost;Port={port};Database=gov_test;Username=postgres;Password=postgres";

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
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            await conn.QueryFirstOrDefaultAsync<GovItem>(_querySql);
        });
    }

    // ── Dapper + StormGate ────────────────────────────────────────────────────
    // StormGate limits to 20 concurrent connection opens — below the server's 25.
    // Remaining tasks wait in the semaphore queue.  Zero server-side rejections.
    // Expected result: succeeds with measured throughput.

    [Benchmark(Baseline = true)]
    public async Task Dapper_StormGate()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = await _stormGate.OpenAsync();
            await conn.QueryFirstOrDefaultAsync<GovItem>(_querySql);
        });
    }

    // ── Uncontrolled EF Core ──────────────────────────────────────────────────
    // Same pool behaviour as uncontrolled Dapper — EF Core's internal NpgsqlDataSource
    // pool defaults to max 100 connections.  Postgres still hard-caps at 25.
    // Expected result: NA (same 53300 crash).

    [Benchmark]
    public async Task EF_Uncontrolled()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new GovEfDbContext(_efOptions);
            await ctx.GovItems.AsNoTracking().FirstOrDefaultAsync();
        });
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
        _stormGate.Dispose();
        await _dapperDataSource.DisposeAsync();

        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync() => await GlobalCleanup();

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
