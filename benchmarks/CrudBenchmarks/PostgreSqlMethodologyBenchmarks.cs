using System.Data;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Dapper;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using pengdows.crud;
using static CrudBenchmarks.PostgreSqlEqualFootingBenchmarks;

namespace CrudBenchmarks;

/// <summary>
/// Measures how much each methodology change moves the PostgreSQL equal-footing numbers. The same
/// workload as <see cref="PostgreSqlEqualFootingBenchmarks"/> runs under several jobs, so each change
/// is the difference between two rows:
///   - Baseline:       today's settings (3 warmups, 10 iterations, workstation GC, nothing pinned).
///   - Pinned:         the database container and the benchmark process on separate cores.
///   - PinnedServerGc: Pinned with server GC, as production ASP.NET Core runs.
///   - PinnedPrecise:  Pinned with BenchmarkDotNet choosing the iteration count for a 1% relative error.
///   - PinnedLatency:  Pinned with network latency added in the container (only when
///                     CRUD_BENCH_NETEM_MS is set, e.g. 0.5).
/// Every case also records what reached the server (pg_stat_statements, reset after warm-up); the
/// run writes results/sqlproof-report.md, which checks that the equal-footing cells send the same
/// statement and one statement per operation.
///
/// Categories: "ReadSingle", "ReadList" and "Create" hold the equal-footing raw-SQL cells (Dapper is
/// the baseline). "ReadSingle-Api" and "Create-Api" hold different workloads, each framework's own
/// API (pengdows RetrieveOneAsync/CreateAsync, EF Core LINQ, compiled query, Add/SaveChanges); compare
/// them with the Dapper row of the matching raw-SQL category.
///
/// Opt-in: dotnet run -c Release -f net10.0 -- --include-opt-in --filter '*PostgreSqlMethodology*'
/// Settings: CRUD_BENCH_DB_CORES (cores given to the database, default 2), CRUD_BENCH_NETEM_MS.
/// </summary>
[OptInBenchmark]
[Config(typeof(MethodologyConfig))]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory, BenchmarkLogicalGroupRule.ByJob)]
[CategoriesColumn]
public class PostgreSqlMethodologyBenchmarks
{
    private const int Ops = 20;
    private const int ListSize = 10;
    private const int SeedRows = 1000;

    internal const string JobVariable = "CRUD_BENCH_METHOD_JOB";
    internal const string CpusetVariable = "CRUD_BENCH_DB_CPUSET";
    internal const string NetemVariable = "CRUD_BENCH_NETEM_MS";

    private const string ReadSingleSql =
        "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id";
    private const string ReadListSql =
        "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > @Age LIMIT @Limit";
    private const string CreateSql =
        "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES (@Name, @Age, @Salary, @IsActive, @CreatedAt)";

    private static readonly Func<EfPgBenchContext, int, Task<EfBenchEntity?>> CompiledReadSingle =
        EF.CompileAsyncQuery((EfPgBenchContext ctx, int id) =>
            ctx.Benchmarks.AsNoTracking().FirstOrDefault(e => e.Id == id));

    private IContainer _container = null!;
    private string _connStr = null!;
    private NpgsqlDataSource _dapperDataSource = null!;
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<BenchEntity, int> _gateway = null!;
    private DbContextOptions<EfPgBenchContext> _efOptions = null!;
    private PooledDbContextFactory<EfPgBenchContext> _efPool = null!;
    private pengdows.crud.ISqlContainer _readSingleSc = null!;
    private pengdows.crud.ISqlContainer _readListSc = null!;

    // What this process measured, for the SQL proof (each case runs in its own process).
    private string? _workload;
    private string? _framework;
    private bool _equalFooting;
    private long _operations;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var cpuset = Environment.GetEnvironmentVariable(CpusetVariable);
        var netemMs = Environment.GetEnvironmentVariable(NetemVariable);

        var builder = new ContainerBuilder()
            .WithImage("postgres:16-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "bench")
            .WithEnvironment("POSTGRES_USER", "bench")
            .WithEnvironment("POSTGRES_DB", "benchmark")
            .WithCommand("-c", "shared_preload_libraries=pg_stat_statements", "-c", "pg_stat_statements.track=top")
            .WithPortBinding(5432, true);
        if (!string.IsNullOrEmpty(cpuset) || !string.IsNullOrEmpty(netemMs))
        {
            builder = builder.WithCreateParameterModifier(p =>
            {
                p.HostConfig ??= new HostConfig();
                if (!string.IsNullOrEmpty(cpuset))
                {
                    p.HostConfig.CpusetCpus = cpuset;
                }

                if (!string.IsNullOrEmpty(netemMs))
                {
                    p.HostConfig.CapAdd = new List<string> { "NET_ADMIN" };
                }
            });
        }

        _container = builder.Build();
        await _container.StartAsync();
        Console.WriteLine($"// methodology job={CurrentJob()} database cpuset={(string.IsNullOrEmpty(cpuset) ? "unpinned" : cpuset)} " +
                          $"latency={(string.IsNullOrEmpty(netemMs) ? "loopback" : netemMs + "ms")}");

        if (!string.IsNullOrEmpty(netemMs))
        {
            var exec = await _container.ExecAsync(new[]
            {
                "sh", "-c", $"apk add --no-cache iproute2-tc >/dev/null && tc qdisc add dev eth0 root netem delay {netemMs}ms"
            });
            if (exec.ExitCode != 0)
            {
                throw new InvalidOperationException($"Could not add {netemMs}ms latency in the container: {exec.Stderr}");
            }
        }

        _connStr =
            $"Host=localhost;Port={_container.GetMappedPublicPort(5432)};Username=bench;Password=bench;Database=benchmark;" +
            "Pooling=true;Minimum Pool Size=5;Maximum Pool Size=100;Timeout=15;CommandTimeout=30;";
        await WaitForReadyAsync(_connStr);

        // Same auto-prepare settings for all three frameworks (DatabaseContext bakes these in).
        var equalConnStr = _connStr + "MaxAutoPrepare=64;AutoPrepareMinUsages=2;";
        _dapperDataSource = new NpgsqlDataSourceBuilder(equalConnStr).Build();

        await using (var conn = await _dapperDataSource.OpenConnectionAsync())
        {
            await conn.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS pg_stat_statements");
            await conn.ExecuteAsync(@"
                CREATE TABLE benchmark (
                    id         SERIAL PRIMARY KEY,
                    name       TEXT NOT NULL,
                    age        INTEGER NOT NULL,
                    salary     DOUBLE PRECISION NOT NULL,
                    is_active  BOOLEAN NOT NULL,
                    created_at TEXT NOT NULL)");
            var seed = new StringBuilder("INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES ");
            var now = DateTime.UtcNow.ToString("O");
            for (var i = 1; i <= SeedRows; i++)
            {
                seed.Append(i > 1 ? "," : string.Empty)
                    .Append($"('Person {i}', {20 + (i % 50)}, {30000.0 + i * 100.0}, {(i % 2 == 0 ? "true" : "false")}, '{now}')");
            }

            await conn.ExecuteAsync(seed.ToString());
        }

        var typeMap = new TypeMapRegistry();
        typeMap.Register<BenchEntity>();
        _pengdowsContext = new DatabaseContext(_connStr, NpgsqlFactory.Instance, typeMap);
        _gateway = new TableGateway<BenchEntity, int>(_pengdowsContext);

        _efOptions = new DbContextOptionsBuilder<EfPgBenchContext>().UseNpgsql(equalConnStr).Options;
        _efPool = new PooledDbContextFactory<EfPgBenchContext>(_efOptions);

        _readSingleSc = _gateway.BuildRetrieve(new[] { 1 });
        _readListSc = _gateway.BuildBaseRetrieve("b");
        _readListSc.Query.Append($" WHERE {_pengdowsContext.WrapObjectName("b.age")} > ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Age"));
        _readListSc.AddParameterWithValue("Age", DbType.Int32, 0);
        _readListSc.Query.Append(" LIMIT ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Limit"));
        _readListSc.AddParameterWithValue("Limit", DbType.Int32, ListSize);

        // Warm every cell past Npgsql's auto-prepare threshold on every pooled connection
        // (5 connections x 2 uses x 2), then start the statistics from zero.
        for (var pass = 0; pass < 20; pass++)
        {
            await ReadSingle_Pengdows();
            await ReadSingle_Dapper();
            await ReadSingle_EntityFramework();
            await ReadSingle_EntityFramework_Pooled();
            await ReadSingle_Pengdows_RetrieveOneAsync();
            await ReadSingle_EntityFramework_Linq();
            await ReadSingle_EntityFramework_Compiled();
            await ReadList_Pengdows();
            await ReadList_Dapper();
            await ReadList_EntityFramework();
            await ReadList_EntityFramework_Pooled();
            await Create_Pengdows();
            await Create_Dapper();
            await Create_EntityFramework();
            await Create_EntityFramework_Pooled();
            await Create_Pengdows_CreateAsync();
            await Create_EntityFramework_SaveChanges();
        }

        _workload = null;
        _framework = null;
        _operations = 0;
        await PgStats.ResetAsync(_connStr);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        try
        {
            if (_workload != null && _framework != null)
            {
                var statements = await PgStats.ReadStatementsAsync(_connStr);
                SqlProofArtifacts.Write(SqlProofArtifacts.DefaultDirectory,
                    new SqlProofCase(_workload, _framework, CurrentJob(), _equalFooting, _operations, statements));
            }
        }
        finally
        {
            await _pengdowsContext.DisposeAsync();
            await _dapperDataSource.DisposeAsync();
            await _container.DisposeAsync();
        }
    }

    private static string CurrentJob() => Environment.GetEnvironmentVariable(JobVariable) ?? "Unknown";

    private void Ran(string workload, string framework, bool equalFooting)
    {
        _workload = workload;
        _framework = framework;
        _equalFooting = equalFooting;
        _operations += Ops;
    }

    private static int IdFor(int i) => (i % SeedRows) + 1;

    private static BenchEntity NewEntity(int i) => new()
    {
        Name = $"Created {i}", Age = 25, Salary = 50000.0, IsActive = true, CreatedAt = DateTime.UtcNow.ToString("O")
    };

    // ======================== ReadSingle: equal-footing raw SQL ========================

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("ReadSingle")]
    public async Task<BenchEntity?> ReadSingle_Pengdows()
    {
        Ran("ReadSingle", "Pengdows", equalFooting: true);
        BenchEntity? result = null;
        for (var i = 0; i < Ops; i++)
        {
            _readSingleSc.SetParameterValue("w0", IdFor(i));
            result = await _gateway.LoadSingleAsync(_readSingleSc);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Baseline = true)]
    [BenchmarkCategory("ReadSingle")]
    public async Task<DapperBenchEntity?> ReadSingle_Dapper()
    {
        Ran("ReadSingle", "Dapper", equalFooting: true);
        DapperBenchEntity? result = null;
        for (var i = 0; i < Ops; i++)
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            result = await conn.QueryFirstOrDefaultAsync<DapperBenchEntity>(ReadSingleSql, new { Id = IdFor(i) });
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("ReadSingle")]
    public async Task<EfBenchEntity?> ReadSingle_EntityFramework()
    {
        Ran("ReadSingle", "EntityFramework", equalFooting: true);
        EfBenchEntity? result = null;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = new EfPgBenchContext(_efOptions);
            result = await ctx.Benchmarks.FromSqlRaw(ReadSingleSql, new NpgsqlParameter("Id", IdFor(i)))
                .AsNoTracking().FirstOrDefaultAsync();
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("ReadSingle")]
    public async Task<EfBenchEntity?> ReadSingle_EntityFramework_Pooled()
    {
        Ran("ReadSingle", "EntityFramework_Pooled", equalFooting: true);
        EfBenchEntity? result = null;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = _efPool.CreateDbContext();
            result = await ctx.Benchmarks.FromSqlRaw(ReadSingleSql, new NpgsqlParameter("Id", IdFor(i)))
                .AsNoTracking().FirstOrDefaultAsync();
        }

        return result;
    }

    // ======================== ReadSingle: each framework's own API ========================

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("ReadSingle-Api")]
    public async Task<BenchEntity?> ReadSingle_Pengdows_RetrieveOneAsync()
    {
        Ran("ReadSingle", "Pengdows_RetrieveOneAsync", equalFooting: false);
        BenchEntity? result = null;
        for (var i = 0; i < Ops; i++)
        {
            result = await _gateway.RetrieveOneAsync(IdFor(i));
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("ReadSingle-Api")]
    public async Task<EfBenchEntity?> ReadSingle_EntityFramework_Linq()
    {
        Ran("ReadSingle", "EntityFramework_Linq", equalFooting: false);
        EfBenchEntity? result = null;
        for (var i = 0; i < Ops; i++)
        {
            var id = IdFor(i);
            await using var ctx = _efPool.CreateDbContext();
            result = await ctx.Benchmarks.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("ReadSingle-Api")]
    public async Task<EfBenchEntity?> ReadSingle_EntityFramework_Compiled()
    {
        Ran("ReadSingle", "EntityFramework_Compiled", equalFooting: false);
        EfBenchEntity? result = null;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = _efPool.CreateDbContext();
            result = await CompiledReadSingle(ctx, IdFor(i));
        }

        return result;
    }

    // ======================== ReadList: equal-footing raw SQL ========================

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("ReadList")]
    public async Task<List<BenchEntity>> ReadList_Pengdows()
    {
        Ran("ReadList", "Pengdows", equalFooting: true);
        List<BenchEntity> result = null!;
        for (var i = 0; i < Ops; i++)
        {
            _readListSc.SetParameterValue("Age", 20 + (i % 30));
            result = await _gateway.LoadListAsync(_readListSc);
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Baseline = true)]
    [BenchmarkCategory("ReadList")]
    public async Task<List<DapperBenchEntity>> ReadList_Dapper()
    {
        Ran("ReadList", "Dapper", equalFooting: true);
        List<DapperBenchEntity> result = null!;
        for (var i = 0; i < Ops; i++)
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            result = (await conn.QueryAsync<DapperBenchEntity>(ReadListSql, new { Age = 20 + (i % 30), Limit = ListSize })).AsList();
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("ReadList")]
    public async Task<List<EfBenchEntity>> ReadList_EntityFramework()
    {
        Ran("ReadList", "EntityFramework", equalFooting: true);
        List<EfBenchEntity> result = null!;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = new EfPgBenchContext(_efOptions);
            result = await ctx.Benchmarks.FromSqlRaw(ReadListSql,
                    new NpgsqlParameter("Age", 20 + (i % 30)), new NpgsqlParameter("Limit", ListSize))
                .AsNoTracking().ToListAsync();
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("ReadList")]
    public async Task<List<EfBenchEntity>> ReadList_EntityFramework_Pooled()
    {
        Ran("ReadList", "EntityFramework_Pooled", equalFooting: true);
        List<EfBenchEntity> result = null!;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = _efPool.CreateDbContext();
            result = await ctx.Benchmarks.FromSqlRaw(ReadListSql,
                    new NpgsqlParameter("Age", 20 + (i % 30)), new NpgsqlParameter("Limit", ListSize))
                .AsNoTracking().ToListAsync();
        }

        return result;
    }

    // ======================== Create: equal-footing raw SQL ========================

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("Create")]
    public async Task<int> Create_Pengdows()
    {
        Ran("Create", "Pengdows", equalFooting: true);
        var count = 0;
        for (var i = 0; i < Ops; i++)
        {
            await using var sc = _gateway.BuildCreate(NewEntity(i));
            count += await sc.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark(OperationsPerInvoke = Ops, Baseline = true)]
    [BenchmarkCategory("Create")]
    public async Task<int> Create_Dapper()
    {
        Ran("Create", "Dapper", equalFooting: true);
        var count = 0;
        for (var i = 0; i < Ops; i++)
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            count += await conn.ExecuteAsync(CreateSql, new
            {
                Name = $"Created {i}", Age = 25, Salary = 50000.0, IsActive = true, CreatedAt = DateTime.UtcNow.ToString("O")
            });
        }

        return count;
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("Create")]
    public async Task<int> Create_EntityFramework()
    {
        Ran("Create", "EntityFramework", equalFooting: true);
        var count = 0;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = new EfPgBenchContext(_efOptions);
            count += await ExecuteEfCreateAsync(ctx, i);
        }

        return count;
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("Create")]
    public async Task<int> Create_EntityFramework_Pooled()
    {
        Ran("Create", "EntityFramework_Pooled", equalFooting: true);
        var count = 0;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = _efPool.CreateDbContext();
            count += await ExecuteEfCreateAsync(ctx, i);
        }

        return count;
    }

    private static Task<int> ExecuteEfCreateAsync(EfPgBenchContext ctx, int i) =>
        ctx.Database.ExecuteSqlRawAsync(CreateSql,
            new NpgsqlParameter("Name", $"Created {i}"),
            new NpgsqlParameter("Age", 25),
            new NpgsqlParameter("Salary", 50000.0),
            new NpgsqlParameter("IsActive", true),
            new NpgsqlParameter("CreatedAt", DateTime.UtcNow.ToString("O")));

    // ======================== Create: each framework's own API ========================

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("Create-Api")]
    public async Task<int> Create_Pengdows_CreateAsync()
    {
        Ran("Create", "Pengdows_CreateAsync", equalFooting: false);
        var count = 0;
        for (var i = 0; i < Ops; i++)
        {
            count += await _gateway.CreateAsync(NewEntity(i)) ? 1 : 0;
        }

        return count;
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("Create-Api")]
    public async Task<int> Create_EntityFramework_SaveChanges()
    {
        Ran("Create", "EntityFramework_SaveChanges", equalFooting: false);
        var count = 0;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = _efPool.CreateDbContext();
            ctx.Benchmarks.Add(new EfBenchEntity
            {
                Name = $"Created {i}", Age = 25, Salary = 50000.0, IsActive = true, CreatedAt = DateTime.UtcNow.ToString("O")
            });
            count += await ctx.SaveChangesAsync();
        }

        return count;
    }

    private static async Task WaitForReadyAsync(string connStr)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var c = new NpgsqlConnection(connStr);
                await c.OpenAsync();
                return;
            }
            catch (NpgsqlException)
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("PostgreSQL container did not become ready within 30 seconds.");
    }

    /// <summary>
    /// The jobs whose differences show each methodology change's effect (see the class comment).
    /// </summary>
    private sealed class MethodologyConfig : ManualConfig
    {
        public MethodologyConfig()
        {
            // The default Error column is the half-width of the 99.9% confidence interval (Mean ± Error).
            // BenchmarkDotNet 0.14's CiLower/CiUpper columns print wrong values here (upper below lower).
            AddColumn(StatisticColumn.Iterations);

            var baseline = Job.Default.WithWarmupCount(3).WithIterationCount(10);
            AddJob(Named(baseline, "Baseline"));

            CpuSplit split;
            try
            {
                split = CpuSplit.For(Environment.ProcessorCount, DatabaseCores());
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Console.WriteLine($"// methodology: no pinned jobs ({ex.Message})");
                return;
            }

            Console.WriteLine($"// methodology: pinned jobs use {split.Describe()}");
            Job Pinned(Job job) => job.WithAffinity(split.BenchmarkAffinity)
                .WithEnvironmentVariable(CpusetVariable, split.DatabaseCpuset);

            AddJob(Named(Pinned(baseline), "Pinned"));
            AddJob(Named(Pinned(baseline).WithGcServer(true), "PinnedServerGc"));
            AddJob(Named(Pinned(Job.Default.WithMinIterationCount(15).WithMaxIterationCount(100).WithMaxRelativeError(0.01)),
                "PinnedPrecise"));

            var netemMs = Environment.GetEnvironmentVariable(NetemVariable);
            if (!string.IsNullOrEmpty(netemMs))
            {
                AddJob(Named(Pinned(baseline).WithEnvironmentVariable(NetemVariable, netemMs), "PinnedLatency"));
            }
        }

        private static Job Named(Job job, string name) => job.WithEnvironmentVariable(JobVariable, name).WithId(name);

        private static int DatabaseCores() =>
            int.TryParse(Environment.GetEnvironmentVariable("CRUD_BENCH_DB_CORES"), out var cores) ? cores : 2;
    }
}
