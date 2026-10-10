using System.Collections.Concurrent;
using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.stormgate;

namespace CrudBenchmarks;

/// <summary>
/// SQL Server counterpart of <see cref="PostgreSqlConnectionGovernanceBenchmarks"/>: the same arms
/// (Dapper and EF Core ungoverned, both behind StormGate, pengdows.crud on its own PoolGovernor at its
/// an explicit ceiling, and pengdows.crud with the opt-in server-ceiling clamp), the same 1,000-way storm.
/// Dapper and EF are left on the SqlClient default pool (100); every pengdows.crud arm gets an explicit
/// ceiling of <see cref="PengdowsCeiling"/> per role, for now.
/// SQL Server's default is 32,767 user connections, so against a stock server nothing can fail and the
/// clamp has nothing to read ("user connections" = 0 means unlimited). The server is therefore capped
/// to <see cref="ServerMaxConnections"/> the way SQL Server requires: sp_configure, then a restart.
/// No measured result is recorded in this comment; read the run's output.
/// </summary>
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3, invocationCount: 5)]
public class SqlServerConnectionGovernanceBenchmarks : IAsyncDisposable
{
    private const string FrameworkDapper = "Dapper";
    private const string FrameworkStormGate = "StormGate";
    private const string FrameworkEntityFramework = "EntityFramework";
    private const string FrameworkPengdows = "Pengdows";
    private const string ScenarioUncontrolled = "Uncontrolled";
    private const string ScenarioGoverned = "Governed";
    private const string ScenarioGovernedEf = "GovernedEf";
    private const string SaPassword = "Benchmark_P@ss1";

    internal const int StormParallelism = PostgreSqlConnectionGovernanceBenchmarks.StormParallelism;
    internal const int StormOperationsPerRun = PostgreSqlConnectionGovernanceBenchmarks.StormOperationsPerRun;
    private const int StormGatePermits = 20;
    private const string FrameworkPengdowsClamped = "PengdowsClamped";
    private const string FrameworkPengdowsClampedHeadroom = "PengdowsClampedHeadroom";
    internal const int Headroom = 2;
    // Every pengdows.crud arm runs with an explicit pool ceiling of this many connections per role, for now.
    // Dapper and EF stay on the provider default (100): they are what is being compared against.
    internal const int PengdowsCeiling = 20;

    // Below the default pool (100), like Postgres's 25, so ungoverned clients can overrun it.
    internal const int ServerMaxConnections = 25;

    private IContainer? _container;
    private string _connStr = string.Empty;
    private StormGate _stormGate = null!;
    private DbContextOptions<GovEfDbContext> _efOptions = null!;
    private DatabaseContext _pengdowsContext = null!;    // PoolGovernor only — NO StormGate
    private TableGateway<GovSqlEntity, int> _pengdowsGateway = null!;
    private DatabaseContext _pengdowsClampedContext = null!;   // same, with ClampPoolsToServerConnectionLimit on
    private TableGateway<GovSqlEntity, int> _pengdowsClampedGateway = null!;
    private DatabaseContext _pengdowsHeadroomContext = null!;  // clamp on, plus ResourceConnectionHeadroom
    private TableGateway<GovSqlEntity, int> _pengdowsHeadroomGateway = null!;

    private const string QuerySql = "SELECT id, val FROM gov_items WHERE id = 1";
    private readonly ConcurrentDictionary<CorrectnessIssueKey, int> _correctnessIssues = new();
    private long _attempted;

    // Dapper and EF run on the SqlClient default (100); the pengdows.crud arms get an explicit ceiling.
    internal static string BuildClientConnectionString(string connectionString) => connectionString;

    // The configuration of every pengdows.crud arm: the same connection string and an explicit ceiling per
    // role; the arms differ only in the opt-in clamp and in the headroom left for other clients.
    internal static DatabaseContextConfiguration PengdowsConfiguration(
        string connectionString, bool clamp = false, int headroom = 0) => new()
    {
        ConnectionString = connectionString,
        ProviderName = "Microsoft.Data.SqlClient",
        DbMode = DbMode.Standard,
        MaxConcurrentReads = PengdowsCeiling,
        MaxConcurrentWrites = PengdowsCeiling,
        ClampPoolsToServerConnectionLimit = clamp,
        ResourceConnectionHeadroom = headroom
    };

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        // A free port chosen up front: a random Docker-assigned one changes when the container restarts.
        int hostPort;
        using (var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
        {
            probe.Start();
            hostPort = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        }

        _container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", SaPassword)
            .WithEnvironment("MSSQL_PID", "Developer")
            .WithPortBinding(hostPort, 1433)   // fixed, so the port survives the restart below
            .Build();
        await _container.StartAsync();

        var port = hostPort;
        var masterConnStr =
            $"Server=localhost,{port};Database=master;User Id=sa;Password={SaPassword};TrustServerCertificate=True;Pooling=false;";
        await WaitForReadyAsync(masterConnStr);

        // Cap the server. "user connections" only takes effect after a restart; the container's
        // filesystem (and so the setting) survives stop/start.
        await using (var master = new SqlConnection(masterConnStr))
        {
            await master.OpenAsync();
            await master.ExecuteAsync($"""
                EXEC sp_configure 'show advanced options', 1; RECONFIGURE;
                EXEC sp_configure 'user connections', {ServerMaxConnections}; RECONFIGURE;
                """);
        }

        await _container.StopAsync();
        await _container.StartAsync();
        await WaitForReadyAsync(masterConnStr);

        await using (var master = new SqlConnection(masterConnStr))
        {
            await master.OpenAsync();
            await master.ExecuteAsync("CREATE DATABASE gov_test");
        }

        // Seed over a non-pooled connection so no idle seed connection lingers in a pool.
        var seedConnStr =
            $"Server=localhost,{port};Database=gov_test;User Id=sa;Password={SaPassword};TrustServerCertificate=True;Pooling=false;";
        await using (var seed = new SqlConnection(seedConnStr))
        {
            await seed.OpenAsync();
            await seed.ExecuteAsync("""
                CREATE TABLE gov_items (id INT IDENTITY(1,1) PRIMARY KEY, val INT NOT NULL);
                INSERT INTO gov_items (val) VALUES (42);
                """);
        }

        _connStr = BuildClientConnectionString(
            $"Server=localhost,{port};Database=gov_test;User Id=sa;Password={SaPassword};TrustServerCertificate=True;");

        _stormGate = StormGate.Create(SqlClientFactory.Instance, _connStr, StormGatePermits, TimeSpan.FromSeconds(30));
        _efOptions = new DbContextOptionsBuilder<GovEfDbContext>().UseSqlServer(_connStr).Options;

        var typeMap = new TypeMapRegistry();
        typeMap.Register<GovSqlEntity>();
        _pengdowsContext = new DatabaseContext(PengdowsConfiguration(_connStr), SqlClientFactory.Instance, null, typeMap);
        _pengdowsGateway = new TableGateway<GovSqlEntity, int>(_pengdowsContext);

        // Identical connection string and load; the only difference is the opt-in clamp, which reads
        // the server's real limit (user connections) and sizes the pools to it.
        _pengdowsClampedContext = new DatabaseContext(
            PengdowsConfiguration(_connStr, clamp: true),
            SqlClientFactory.Instance, null, typeMap);
        _pengdowsClampedGateway = new TableGateway<GovSqlEntity, int>(_pengdowsClampedContext);

        // SQL Server has no admin reserve to leave slots free, so the same clamp with a configured
        // headroom shows whether leaving a couple of slots for the context's own idle connections helps.
        _pengdowsHeadroomContext = new DatabaseContext(
            PengdowsConfiguration(_connStr, clamp: true, headroom: Headroom),
            SqlClientFactory.Instance, null, typeMap);
        _pengdowsHeadroomGateway = new TableGateway<GovSqlEntity, int>(_pengdowsHeadroomContext);

        Console.WriteLine($"[GOV-SQLSERVER] server user connections={ServerMaxConnections}, pool max=default (pengdows arms: {PengdowsCeiling} per role), StormGate permits={StormGatePermits}, " +
                          $"parallelism={StormParallelism}, operations={StormOperationsPerRun}");
    }

    [Benchmark]
    [CorrectnessIdentity(FrameworkDapper, ScenarioUncontrolled)]
    public async Task Dapper_Uncontrolled()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(StormOperationsPerRun, StormParallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();
            var item = await conn.QueryFirstOrDefaultAsync<GovItem>(QuerySql);
            if (item == null)
            {
                MarkInvalid(ScenarioUncontrolled, FrameworkDapper, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioUncontrolled, FrameworkDapper, $"Exception: {ex.GetType().Name}"));
    }

    [Benchmark(Baseline = true)]
    [CorrectnessIdentity(FrameworkStormGate, ScenarioGoverned)]
    public async Task Dapper_StormGate()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(StormOperationsPerRun, StormParallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
            await using var conn = await _stormGate.OpenAsync();
            var item = await conn.QueryFirstOrDefaultAsync<GovItem>(QuerySql);
            if (item == null)
            {
                MarkInvalid(ScenarioGoverned, FrameworkStormGate, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioGoverned, FrameworkStormGate, $"Exception: {ex.GetType().Name}"));
    }

    [Benchmark]
    [CorrectnessIdentity(FrameworkEntityFramework, ScenarioUncontrolled)]
    public async Task EF_Uncontrolled()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(StormOperationsPerRun, StormParallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
            await using var ctx = new GovEfDbContext(_efOptions);
            var item = await ctx.GovItems.AsNoTracking().FirstOrDefaultAsync();
            if (item == null)
            {
                MarkInvalid(ScenarioUncontrolled, FrameworkEntityFramework, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioUncontrolled, FrameworkEntityFramework, $"Exception: {ex.GetType().Name}"));
    }

    [Benchmark]
    [CorrectnessIdentity(FrameworkStormGate, ScenarioGovernedEf)]
    public async Task EF_StormGate()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(StormOperationsPerRun, StormParallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
            await using var conn = await _stormGate.OpenAsync();
            var options = new DbContextOptionsBuilder<GovEfDbContext>().UseSqlServer(conn).Options;
            await using var ctx = new GovEfDbContext(options);
            var item = await ctx.GovItems.AsNoTracking().FirstOrDefaultAsync();
            if (item == null)
            {
                MarkInvalid(ScenarioGovernedEf, FrameworkStormGate, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioGovernedEf, FrameworkStormGate, $"Exception: {ex.GetType().Name}"));
    }

    // pengdows.crud on its own PoolGovernor (sized from the connection string), no StormGate.
    [Benchmark]
    [CorrectnessIdentity(FrameworkPengdows, ScenarioGoverned)]
    public async Task Pengdows_Governed()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(StormOperationsPerRun, StormParallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
            await using var sc = _pengdowsGateway.BuildRetrieve(new[] { 1 });
            var item = await _pengdowsGateway.LoadSingleAsync(sc);
            if (item == null)
            {
                MarkInvalid(ScenarioGoverned, FrameworkPengdows, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioGoverned, FrameworkPengdows, $"Exception: {ex.GetType().Name}"));
    }

    [Benchmark]
    [CorrectnessIdentity(FrameworkPengdowsClamped, ScenarioGoverned)]
    public async Task Pengdows_Clamped()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(StormOperationsPerRun, StormParallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
            await using var sc = _pengdowsClampedGateway.BuildRetrieve(new[] { 1 });
            var item = await _pengdowsClampedGateway.LoadSingleAsync(sc);
            if (item == null)
            {
                MarkInvalid(ScenarioGoverned, FrameworkPengdowsClamped, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioGoverned, FrameworkPengdowsClamped, $"Exception: {ex.GetType().Name}"));
    }

    [Benchmark]
    [CorrectnessIdentity(FrameworkPengdowsClampedHeadroom, ScenarioGoverned)]
    public async Task Pengdows_ClampedHeadroom()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(StormOperationsPerRun, StormParallelism, async () =>
        {
            Interlocked.Increment(ref _attempted);
            await using var sc = _pengdowsHeadroomGateway.BuildRetrieve(new[] { 1 });
            var item = await _pengdowsHeadroomGateway.LoadSingleAsync(sc);
            if (item == null)
            {
                MarkInvalid(ScenarioGoverned, FrameworkPengdowsClampedHeadroom, "Query returned null");
            }
        }, ex => MarkInvalid(ScenarioGoverned, FrameworkPengdowsClampedHeadroom, $"Exception: {ex.GetType().Name}"));
    }

    private static async Task WaitForReadyAsync(string connStr)
    {
        for (var i = 0; i < 120; i++)
        {
            try
            {
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("SQL Server container did not become ready in time.");
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        BenchmarkCorrectnessArtifacts.Write(nameof(SqlServerConnectionGovernanceBenchmarks),
            _correctnessIssues
                .OrderBy(pair => pair.Key.ParameterKey, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Scenario, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Framework, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Reason, StringComparer.Ordinal)
                .Select(pair => new CorrectnessIssue(
                    pair.Key.ParameterKey == "*" ? null : pair.Key.ParameterKey,
                    pair.Key.Scenario, pair.Key.Framework, pair.Key.Reason, pair.Value))
                .ToArray(),
            Interlocked.Read(ref _attempted));
        Console.WriteLine($"[GOV-SQLSERVER] process {Environment.ProcessId} attempted {Interlocked.Read(ref _attempted)} operations");

        _pengdowsHeadroomContext?.Dispose();
        _pengdowsClampedContext?.Dispose();
        _pengdowsContext?.Dispose();
        _stormGate?.Dispose();
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

    [Table("gov_items")]
    public class GovSqlEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("val", DbType.Int32)]
        public int Val { get; set; }
    }
}
