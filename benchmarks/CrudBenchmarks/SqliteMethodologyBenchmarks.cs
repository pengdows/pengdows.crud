using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using pengdows.crud;
using static CrudBenchmarks.EqualFootingCrudBenchmarks;

namespace CrudBenchmarks;

/// <summary>
/// The SQLite counterpart of <see cref="PostgreSqlMethodologyBenchmarks"/>'s framework-API cells
/// (REL-006), so the "each framework's own API" comparison exists on SQLite too.
///
/// Categories: "ReadSingle" and "Create" hold the equal-footing raw-SQL cells (Dapper is the
/// baseline; EF Core runs the same SQL through FromSqlRaw/ExecuteSqlRaw, new and pooled contexts).
/// "ReadSingle-Api" and "Create-Api" hold each framework's own API: pengdows
/// RetrieveOneAsync/CreateAsync, EF Core LINQ and a compiled query, Add/SaveChanges. Compare those
/// with the Dapper row of the matching raw-SQL category.
///
/// The PostgreSQL class's methodology jobs (CPU pinning, server GC, injected latency) are about a
/// database in another process; SQLite runs in-process, so this class keeps one job. Every framework
/// opens and closes its connection per operation against one shared-cache in-memory database kept
/// alive by a sentinel connection, as <see cref="EqualFootingCrudBenchmarks"/> does.
///
/// Opt-in: dotnet run -c Release -f net10.0 -- --include-opt-in --filter '*SqliteMethodology*'
/// </summary>
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SqliteMethodologyBenchmarks
{
    private const int Ops = 20;
    private const int SeedRows = 1000;
    private const string ConnStr = "Data Source=SqliteMethodologyBench;Mode=Memory;Cache=Shared";

    private const string ReadSingleSql =
        "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id";
    private const string CreateSql =
        "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES (@Name, @Age, @Salary, @IsActive, @CreatedAt)";

    private static readonly Func<EfBenchContext, int, Task<EfBenchEntity?>> CompiledReadSingle =
        EF.CompileAsyncQuery((EfBenchContext ctx, int id) =>
            ctx.Benchmarks.AsNoTracking().FirstOrDefault(e => e.Id == id));

    private SqliteConnection _sentinel = null!;
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<BenchEntity, int> _gateway = null!;
    private DbContextOptions<EfBenchContext> _efOptions = null!;
    private PooledDbContextFactory<EfBenchContext> _efPool = null!;
    private ISqlContainer _readSingleSc = null!;
    private bool _originalMatchNamesWithUnderscores;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _originalMatchNamesWithUnderscores = DefaultTypeMap.MatchNamesWithUnderscores;
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _sentinel = new SqliteConnection(ConnStr);
        _sentinel.Open();
        await using (var cmd = _sentinel.CreateCommand())
        {
            cmd.CommandText = @"
                DROP TABLE IF EXISTS benchmark;
                CREATE TABLE benchmark (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    age INTEGER NOT NULL,
                    salary REAL NOT NULL,
                    is_active INTEGER NOT NULL,
                    created_at TEXT NOT NULL)";
            cmd.ExecuteNonQuery();
        }

        await using (var tx = _sentinel.BeginTransaction())
        {
            await using var cmd = _sentinel.CreateCommand();
            cmd.Transaction = tx;
            var now = DateTime.UtcNow.ToString("O");
            for (var i = 1; i <= SeedRows; i++)
            {
                cmd.CommandText =
                    "INSERT INTO benchmark (name, age, salary, is_active, created_at) " +
                    $"VALUES ('Person {i}', {20 + (i % 50)}, {30000.0 + i * 100.0}, {(i % 2 == 0 ? 1 : 0)}, '{now}')";
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        var typeMap = new TypeMapRegistry();
        typeMap.Register<BenchEntity>();
        _pengdowsContext = new DatabaseContext(ConnStr, SqliteFactory.Instance, typeMap);
        _gateway = new TableGateway<BenchEntity, int>(_pengdowsContext);
        _readSingleSc = _gateway.BuildRetrieve(new[] { 1 });

        _efOptions = new DbContextOptionsBuilder<EfBenchContext>().UseSqlite(ConnStr).Options;
        _efPool = new PooledDbContextFactory<EfBenchContext>(_efOptions);

        // Warm every cell (plan caches, EF model and compiled query) before measuring.
        for (var pass = 0; pass < 5; pass++)
        {
            await ReadSingle_Pengdows();
            await ReadSingle_Dapper();
            await ReadSingle_EntityFramework();
            await ReadSingle_EntityFramework_Pooled();
            await ReadSingle_Pengdows_RetrieveOneAsync();
            await ReadSingle_EntityFramework_Linq();
            await ReadSingle_EntityFramework_Compiled();
            await Create_Pengdows();
            await Create_Dapper();
            await Create_EntityFramework();
            await Create_EntityFramework_Pooled();
            await Create_Pengdows_CreateAsync();
            await Create_EntityFramework_SaveChanges();
        }
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = _originalMatchNamesWithUnderscores;
        await _readSingleSc.DisposeAsync();
        await _pengdowsContext.DisposeAsync();
        await _sentinel.DisposeAsync();
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
        DapperBenchEntity? result = null;
        for (var i = 0; i < Ops; i++)
        {
            await using var conn = new SqliteConnection(ConnStr);
            await conn.OpenAsync();
            result = await conn.QueryFirstOrDefaultAsync<DapperBenchEntity>(ReadSingleSql, new { Id = IdFor(i) });
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("ReadSingle")]
    public async Task<EfBenchEntity?> ReadSingle_EntityFramework()
    {
        EfBenchEntity? result = null;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = new EfBenchContext(_efOptions);
            result = await ctx.Benchmarks.FromSqlRaw(ReadSingleSql, new SqliteParameter("@Id", IdFor(i)))
                .AsNoTracking().FirstOrDefaultAsync();
        }

        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("ReadSingle")]
    public async Task<EfBenchEntity?> ReadSingle_EntityFramework_Pooled()
    {
        EfBenchEntity? result = null;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = _efPool.CreateDbContext();
            result = await ctx.Benchmarks.FromSqlRaw(ReadSingleSql, new SqliteParameter("@Id", IdFor(i)))
                .AsNoTracking().FirstOrDefaultAsync();
        }

        return result;
    }

    // ======================== ReadSingle: each framework's own API ========================

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("ReadSingle-Api")]
    public async Task<BenchEntity?> ReadSingle_Pengdows_RetrieveOneAsync()
    {
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
        EfBenchEntity? result = null;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = _efPool.CreateDbContext();
            result = await CompiledReadSingle(ctx, IdFor(i));
        }

        return result;
    }

    // ======================== Create: equal-footing raw SQL ========================

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("Create")]
    public async Task<int> Create_Pengdows()
    {
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
        var count = 0;
        for (var i = 0; i < Ops; i++)
        {
            await using var conn = new SqliteConnection(ConnStr);
            await conn.OpenAsync();
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
        var count = 0;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = new EfBenchContext(_efOptions);
            count += await ExecuteEfCreateAsync(ctx, i);
        }

        return count;
    }

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("Create")]
    public async Task<int> Create_EntityFramework_Pooled()
    {
        var count = 0;
        for (var i = 0; i < Ops; i++)
        {
            await using var ctx = _efPool.CreateDbContext();
            count += await ExecuteEfCreateAsync(ctx, i);
        }

        return count;
    }

    private static Task<int> ExecuteEfCreateAsync(EfBenchContext ctx, int i) =>
        ctx.Database.ExecuteSqlRawAsync(CreateSql,
            new SqliteParameter("@Name", $"Created {i}"),
            new SqliteParameter("@Age", 25),
            new SqliteParameter("@Salary", 50000.0),
            new SqliteParameter("@IsActive", true),
            new SqliteParameter("@CreatedAt", DateTime.UtcNow.ToString("O")));

    // ======================== Create: each framework's own API ========================

    [Benchmark(OperationsPerInvoke = Ops)]
    [BenchmarkCategory("Create-Api")]
    public async Task<int> Create_Pengdows_CreateAsync()
    {
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
}
