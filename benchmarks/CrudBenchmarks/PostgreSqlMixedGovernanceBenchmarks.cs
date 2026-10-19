using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.stormgate;
using static CrudBenchmarks.PostgreSqlConnectionGovernanceBenchmarks;

namespace CrudBenchmarks;

/// <summary>Which pengdows.crud configuration a mixed-storm arm runs.</summary>
public enum MixedArm
{
    /// <summary>20 per role, no clamp, StormGate's 30 s wait: the pools together can reach 40 against a server that allows 25.</summary>
    Governed,

    /// <summary>11 per role by hand, no clamp: the clamp's first design, a static division of the 22 usable connections.</summary>
    Governed11,

    /// <summary>The same sizes as <see cref="Governed"/> with the clamp on. On 2.0.6 that divides the usable connections between the roles in advance; on the POOL-003 branch each role keeps 20 and they share one per-server budget.</summary>
    Clamped
}

/// <summary>
/// MIXED READ/WRITE storm against real PostgreSQL (max_connections=25, 22 usable), the case every other storm arm
/// leaves out: they only read, so none of them can show the failure the clamp exists to prevent (the reader and writer
/// pools together refused by the server) or what a shared per-server budget buys over dividing the 22 in advance.
/// 1,500 calls of 300 ms from <see cref="Callers"/> concurrent callers (50, just over the 22 usable connections, and
/// 1,000); ReadPercent of them read and the rest write. The pengdows.crud arms read through the reader pool and write
/// through the writer pool (ExecuteNonQuery). Dapper and EF run the same SQL on one provider pool (Npgsql's default 100),
/// either with nothing in front of it or behind StormGate (20 permits, 30 s). Every governed arm waits as long as
/// StormGate does (30 s), so a failure here is a refusal or a timeout the wait did not hide, not the 5 s default. Each
/// arm runs in its own process; the failure counts by exception type and the latency of successes and of failures
/// (P50/P95/P99, from the call's start to its end, queueing included) are printed (<c>[MIX]</c> lines) and written to a
/// sidecar file, because the shared correctness table is not authoritative for them.
/// </summary>
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3, invocationCount: 1)]
public class PostgreSqlMixedGovernanceBenchmarks : IAsyncDisposable
{
    internal const int Operations = 1500;
    internal const string WriteSql = "SELECT pg_sleep(0.3)";

    [Params(50, 90)]
    public int ReadPercent { get; set; }

    [Params(50, 1000)]
    public int Callers { get; set; }

    internal static bool IsRead(int index, int readPercent) => index % 100 < readPercent;

    internal static DatabaseContextConfiguration MixedConfiguration(string connectionString, MixedArm arm)
    {
        var size = arm == MixedArm.Governed11 ? ClampedShare : PengdowsCeiling;
        return PengdowsConfiguration(connectionString, clamp: arm == MixedArm.Clamped,
            poolAcquireTimeout: StormGateAcquireTimeout, reads: size, writes: size);
    }

    private static readonly string PgPassword =
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));

    private IContainer? _container;
    private string _connStr = string.Empty;
    private DatabaseContext _governedContext = null!;
    private DatabaseContext _governed11Context = null!;
    private DatabaseContext _clampedContext = null!;
    private TableGateway<GovPgEntity, int> _governedGateway = null!;
    private TableGateway<GovPgEntity, int> _governed11Gateway = null!;
    private TableGateway<GovPgEntity, int> _clampedGateway = null!;
    private NpgsqlDataSource _providerDataSource = null!;   // Npgsql's default pool (100), above the server's 25
    private StormGate _stormGate = null!;                    // 20 permits, 30 s: the only admission control Dapper/EF get
    private DbContextOptions<GovEfDbContext> _efOptions = null!;
    private readonly ConcurrentDictionary<string, int> _failures = new();
    private readonly ConcurrentBag<long> _successTicks = new();
    private readonly ConcurrentBag<long> _failureTicks = new();
    private long _attempted;
    private long _reads;
    private long _writes;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        // The previous case may have been an uncontrolled storm; let its TIME_WAIT sockets drain before starting a container.
        await TimeWaitDrain.WaitForHostAsync("MIX");
        try
        {
            await SetUpAsync();
        }
        catch
        {
            // A setup that throws gets no GlobalCleanup: dispose the container here or it outlives the run.
            await DisposeContainerAsync();
            throw;
        }
    }

    private async Task SetUpAsync()
    {
        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", PgPassword)
            .WithEnvironment("POSTGRES_DB", "gov_test")
            .WithPortBinding(BenchmarkHostPort.ReserveBelowEphemeralRange(), 5432)
            .WithCommand("-c", $"max_connections={ServerMaxConnections}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();
        await _container.StartAsync();

        _connStr = $"Host=localhost;Port={_container.GetMappedPublicPort(5432)};Database=gov_test;Username=postgres;Password={PgPassword}";
        await SeedAsync();

        var typeMap = new TypeMapRegistry();
        typeMap.Register<GovPgEntity>();
        _governedContext = new DatabaseContext(MixedConfiguration(_connStr, MixedArm.Governed), NpgsqlFactory.Instance, null, typeMap);
        _governed11Context = new DatabaseContext(MixedConfiguration(_connStr, MixedArm.Governed11), NpgsqlFactory.Instance, null, typeMap);
        _clampedContext = new DatabaseContext(MixedConfiguration(_connStr, MixedArm.Clamped), NpgsqlFactory.Instance, null, typeMap);
        _governedGateway = new TableGateway<GovPgEntity, int>(_governedContext);
        _governed11Gateway = new TableGateway<GovPgEntity, int>(_governed11Context);
        _clampedGateway = new TableGateway<GovPgEntity, int>(_clampedContext);

        _providerDataSource = NpgsqlDataSource.Create(_connStr);
        _stormGate = new StormGate(_providerDataSource, StormGatePermits, StormGateAcquireTimeout);
        _efOptions = new DbContextOptionsBuilder<GovEfDbContext>().UseNpgsql(_connStr).Options;

        Console.WriteLine($"[MIX] postgres max_connections={ServerMaxConnections}, callers={Callers}, ops={Operations}, " +
                          $"read%={ReadPercent}, wait={StormGateAcquireTimeout.TotalSeconds:F0}s");
    }

    [Benchmark]
    public Task Pengdows_Governed() => RunPengdowsAsync(_governedContext, _governedGateway, "Governed");

    [Benchmark]
    public Task Pengdows_Governed11() => RunPengdowsAsync(_governed11Context, _governed11Gateway, "Governed11");

    [Benchmark]
    public Task Pengdows_Clamped() => RunPengdowsAsync(_clampedContext, _clampedGateway, "Clamped");

    [Benchmark]
    public Task Dapper_Uncontrolled() => RunDapperAsync(async () => await _providerDataSource.OpenConnectionAsync(), "DapperUncontrolled");

    [Benchmark]
    public Task Dapper_StormGate() => RunDapperAsync(() => _stormGate.OpenAsync(), "DapperStormGate");

    [Benchmark]
    public Task EF_Uncontrolled() => RunAsync("EFUncontrolled", async (isRead, readSql) =>
    {
        await using var ctx = new GovEfDbContext(_efOptions);
        await EfCallAsync(ctx, isRead, readSql, "EFUncontrolled");
    });

    // EF on a StormGate-issued connection (UseNpgsql(DbConnection)), as PostgreSqlConnectionGovernanceBenchmarks.EF_StormGate
    // does. EF does not dispose a connection it was handed, so the call owns it.
    [Benchmark]
    public Task EF_StormGate() => RunAsync("EFStormGate", async (isRead, readSql) =>
    {
        await using var connection = await _stormGate.OpenAsync();
        await using var ctx = new GovEfDbContext(new DbContextOptionsBuilder<GovEfDbContext>().UseNpgsql(connection).Options);
        await EfCallAsync(ctx, isRead, readSql, "EFStormGate");
    });

    private Task RunPengdowsAsync(DatabaseContext context, TableGateway<GovPgEntity, int> gateway, string arm) =>
        RunAsync(arm, async (isRead, readSql) =>
        {
            if (isRead)
            {
                await using var read = context.CreateSqlContainer(readSql);
                if (await gateway.LoadSingleAsync(read) == null)
                {
                    Fail(arm, "Query returned null");
                }
            }
            else
            {
                await using var write = context.CreateSqlContainer(WriteSql);
                await write.ExecuteNonQueryAsync();
            }
        });

    private Task RunDapperAsync(Func<Task<System.Data.Common.DbConnection>> open, string arm) =>
        RunAsync(arm, async (isRead, readSql) =>
        {
            await using var connection = await open();
            if (isRead)
            {
                if (await connection.QueryFirstOrDefaultAsync<GovItem>(readSql) == null)
                {
                    Fail(arm, "Query returned null");
                }
            }
            else
            {
                await connection.ExecuteAsync(WriteSql);
            }
        });

    private async Task EfCallAsync(GovEfDbContext ctx, bool isRead, string readSql, string arm)
    {
        if (isRead)
        {
            if (await ctx.GovItems.FromSqlRaw(readSql).AsNoTracking().FirstOrDefaultAsync() == null)
            {
                Fail(arm, "Query returned null");
            }
        }
        else
        {
            await ctx.Database.ExecuteSqlRawAsync(WriteSql);
        }
    }

    // Times every call from its start (queueing for a slot or a permit included) to its end; successes and failures apart.
    private async Task RunAsync(string arm, Func<bool, string, Task> call)
    {
        var readSql = QuerySqlFor(SlowWorkMilliseconds);
        var readPercent = ReadPercent;
        var next = -1;
        await BenchmarkConcurrency.RunConcurrentWithErrors(Operations, Callers, async () =>
        {
            Interlocked.Increment(ref _attempted);
            var isRead = IsRead(Interlocked.Increment(ref next) % Operations, readPercent);
            if (isRead)
            {
                Interlocked.Increment(ref _reads);
            }
            else
            {
                Interlocked.Increment(ref _writes);
            }

            var started = Stopwatch.GetTimestamp();
            try
            {
                await call(isRead, readSql);
                _successTicks.Add(Stopwatch.GetTimestamp() - started);
            }
            catch
            {
                _failureTicks.Add(Stopwatch.GetTimestamp() - started);
                throw;
            }
        }, ex => Fail(arm, ex.GetType().Name));
    }

    private void Fail(string arm, string reason) =>
        _failures.AddOrUpdate($"{arm}: {reason}", 1, static (_, current) => current + 1);

    private async Task SeedAsync()
    {
        await using var data = NpgsqlDataSource.Create(_connStr);
        for (var i = 0; i < 60; i++)
        {
            try
            {
                await using var probe = await data.OpenConnectionAsync();
                break;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        await using var conn = await data.OpenConnectionAsync();
        await using var command = conn.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS gov_items (id SERIAL PRIMARY KEY, val INTEGER NOT NULL); " +
                              "INSERT INTO gov_items (val) VALUES (42);";
        await command.ExecuteNonQueryAsync();
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        var attempted = Interlocked.Read(ref _attempted);
        var failed = _failures.Values.Sum();
        var sb = new StringBuilder();
        sb.AppendLine("# PostgreSqlMixedGovernanceBenchmarks, one process");
        sb.AppendLine();
        sb.AppendLine($"read% {ReadPercent}, callers {Callers}, wait {StormGateAcquireTimeout.TotalSeconds:F0} s, server max_connections {ServerMaxConnections}");
        sb.AppendLine($"Attempted {attempted} (reads {Interlocked.Read(ref _reads)}, writes {Interlocked.Read(ref _writes)})");
        sb.AppendLine($"Failed {failed} ({(attempted == 0 ? 0 : 100.0 * failed / attempted):F2}%)");
        foreach (var pair in _failures.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"- {pair.Key}: {pair.Value}");
        }

        sb.AppendLine();
        var latency = StormLatencySummary.Format(_successTicks, _failureTicks);
        sb.Append(latency);

        Console.WriteLine($"[MIX] process {Environment.ProcessId} attempted {attempted} failed {failed}");
        foreach (var pair in _failures.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"[MIX]   {pair.Key}: {pair.Value}");
        }

        Console.Write(latency);

        var directory = Path.Combine("BenchmarkDotNet.Artifacts", "results");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory,
            $"PostgreSqlMixedGovernanceBenchmarks-{Environment.ProcessId}-read{ReadPercent}-callers{Callers}.md"), sb.ToString());

        _governedContext?.Dispose();
        _governed11Context?.Dispose();
        _clampedContext?.Dispose();
        _stormGate?.Dispose();
        _providerDataSource?.Dispose();
        await DisposeContainerAsync();
    }

    private async Task DisposeContainerAsync()
    {
        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
            _container = null;
        }
    }

    public async ValueTask DisposeAsync() => await GlobalCleanup();
}
