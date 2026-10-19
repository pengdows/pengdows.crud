using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using pengdows.crud;
using BenchEntity = CrudBenchmarks.PostgreSqlEqualFootingBenchmarks.BenchEntity;
using DapperBenchEntity = CrudBenchmarks.PostgreSqlEqualFootingBenchmarks.DapperBenchEntity;
using EfBenchEntity = CrudBenchmarks.PostgreSqlEqualFootingBenchmarks.EfBenchEntity;
using EfPgBenchContext = CrudBenchmarks.PostgreSqlEqualFootingBenchmarks.EfPgBenchContext;

namespace CrudBenchmarks;

/// <summary>
/// Fixed cost per query against cost per row, on a real PostgreSQL: <see cref="PostgreSqlEqualFootingBenchmarks"/>'
/// ReadList and FilteredQuery arms (same SQL, same per-call pooled open, same auto-prepare settings) at 1 to 10,000
/// rows. Time and allocations at each size give each library's intercept and its per-row slope; the per-row cost over
/// each interval, (T(1000) - T(100)) / 900 and (T(10000) - T(1000)) / 9000, shows whether it holds as results grow.
/// Opt-in: <c>--filter "*PostgreSqlRowScaling*"</c>.
/// </summary>
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class PostgreSqlRowScalingBenchmarks : IDisposable
{
    internal const string ReadListSql =
        "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > @Age LIMIT @Limit";

    internal const string FilteredQuerySql =
        "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE is_active = @IsActive AND age >= @MinAge AND age <= @MaxAge LIMIT @Limit";

    private IContainer _container = null!;
    private string _connStr = null!;
    private NpgsqlDataSource _dapperDataSource = null!;
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<BenchEntity, int> _gateway = null!;
    private DbContextOptions<EfPgBenchContext> _efOptions = null!;
    private ISqlContainer _readListSc = null!;
    private ISqlContainer _filteredQuerySc = null!;
    private bool _originalMatchNamesWithUnderscores;

    [Params(1, 10, 100, 1000, 10000)] public int RecordCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _originalMatchNamesWithUnderscores = DefaultTypeMap.MatchNamesWithUnderscores;
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _container = new ContainerBuilder()
            .WithImage("postgres:16-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "bench")
            .WithEnvironment("POSTGRES_USER", "bench")
            .WithEnvironment("POSTGRES_DB", "benchmark")
            .WithPortBinding(BenchmarkHostPort.ReserveBelowEphemeralRange(), 5432)
            .Build();
        await _container.StartAsync();

        _connStr =
            $"Host=localhost;Port={_container.GetMappedPublicPort(5432)};Username=bench;Password=bench;Database=benchmark;" +
            "Pooling=true;Minimum Pool Size=5;Maximum Pool Size=100;Timeout=15;CommandTimeout=30;";
        await WaitForReadyAsync(_connStr);

        // The same auto-prepare settings DatabaseContext bakes in, for Dapper and EF alike.
        var equalConnStr = _connStr + "MaxAutoPrepare=64;AutoPrepareMinUsages=2;";
        _dapperDataSource = new NpgsqlDataSourceBuilder(equalConnStr).Build();
        await SeedAsync();

        var typeMap = new TypeMapRegistry();
        typeMap.Register<BenchEntity>();
        _pengdowsContext = new DatabaseContext(_connStr, NpgsqlFactory.Instance, typeMap);
        _gateway = new TableGateway<BenchEntity, int>(_pengdowsContext);
        _efOptions = new DbContextOptionsBuilder<EfPgBenchContext>().UseNpgsql(equalConnStr).Options;

        _readListSc = _gateway.BuildBaseRetrieve("b");
        _readListSc.Query.Append($" WHERE {_pengdowsContext.WrapObjectName("b.age")} > ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Age"));
        _readListSc.AddParameterWithValue("Age", DbType.Int32, RowScalingSeed.ReadListMinAgeExclusive);
        _readListSc.Query.Append(" LIMIT ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Limit"));
        _readListSc.AddParameterWithValue("Limit", DbType.Int32, RecordCount);

        _filteredQuerySc = _gateway.BuildBaseRetrieve("b");
        _filteredQuerySc.Query.Append($" WHERE {_pengdowsContext.WrapObjectName("b.is_active")} = ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("IsActive"));
        _filteredQuerySc.AddParameterWithValue("IsActive", DbType.Boolean, true);
        _filteredQuerySc.Query.Append($" AND {_pengdowsContext.WrapObjectName("b.age")} >= ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("MinAge"));
        _filteredQuerySc.AddParameterWithValue("MinAge", DbType.Int32, RowScalingSeed.FilteredMinAge);
        _filteredQuerySc.Query.Append($" AND {_pengdowsContext.WrapObjectName("b.age")} <= ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("MaxAge"));
        _filteredQuerySc.AddParameterWithValue("MaxAge", DbType.Int32, RowScalingSeed.FilteredMaxAge);
        _filteredQuerySc.Query.Append(" LIMIT ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("Limit"));
        _filteredQuerySc.AddParameterWithValue("Limit", DbType.Int32, RecordCount);

        await ValidateAsync();
    }

    // Every arm must return exactly RecordCount fully hydrated rows, or the comparison measures different work.
    private async Task ValidateAsync()
    {
        RowScalingValidation.Check("ReadList_Pengdows", RecordCount, await ReadList_Pengdows(),
            e => (e.Name, e.CreatedAt, e.Age > RowScalingSeed.ReadListMinAgeExclusive));
        RowScalingValidation.Check("ReadList_Dapper", RecordCount, await ReadList_Dapper(),
            e => (e.Name, e.CreatedAt, e.Age > RowScalingSeed.ReadListMinAgeExclusive));
        RowScalingValidation.Check("ReadList_EntityFramework", RecordCount, await ReadList_EntityFramework(),
            e => (e.Name, e.CreatedAt, e.Age > RowScalingSeed.ReadListMinAgeExclusive));
        RowScalingValidation.Check("FilteredQuery_Pengdows", RecordCount, await FilteredQuery_Pengdows(),
            e => (e.Name, e.CreatedAt, e.IsActive));
        RowScalingValidation.Check("FilteredQuery_Dapper", RecordCount, await FilteredQuery_Dapper(),
            e => (e.Name, e.CreatedAt, e.IsActive));
        RowScalingValidation.Check("FilteredQuery_EntityFramework", RecordCount, await FilteredQuery_EntityFramework(),
            e => (e.Name, e.CreatedAt, e.IsActive));
    }

    private async Task SeedAsync()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS benchmark (
                    id             SERIAL PRIMARY KEY,
                    name           TEXT NOT NULL,
                    age            INTEGER NOT NULL,
                    salary         DOUBLE PRECISION NOT NULL,
                    is_active      BOOLEAN NOT NULL,
                    created_at     TEXT NOT NULL
                )";
            await cmd.ExecuteNonQueryAsync();
        }

        var now = DateTime.UtcNow.ToString("O");
        foreach (var insert in RowScalingSeed.InsertStatements(RowScalingSeed.Rows, i =>
                     $"('Person {i}', {RowScalingSeed.Age(i)}, {30000.0 + i * 100.0}, " +
                     $"{(RowScalingSeed.IsActive(i) ? "true" : "false")}, '{now}')"))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = insert;
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var analyze = conn.CreateCommand())
        {
            analyze.CommandText = "ANALYZE benchmark";
            await analyze.ExecuteNonQueryAsync();
        }
    }

    [Benchmark]
    public async Task<List<BenchEntity>> ReadList_Pengdows()
    {
        _readListSc.SetParameterValue("Age", RowScalingSeed.ReadListMinAgeExclusive);
        return await _gateway.LoadListAsync(_readListSc);
    }

    [Benchmark]
    public async Task<List<DapperBenchEntity>> ReadList_Dapper()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<DapperBenchEntity>(ReadListSql,
            new { Age = RowScalingSeed.ReadListMinAgeExclusive, Limit = RecordCount });
        return rows.ToList();
    }

    [Benchmark]
    public async Task<List<EfBenchEntity>> ReadList_EntityFramework()
    {
        await using var ctx = new EfPgBenchContext(_efOptions);
        return await ctx.Benchmarks
            .FromSqlRaw(ReadListSql,
                new NpgsqlParameter("Age", RowScalingSeed.ReadListMinAgeExclusive),
                new NpgsqlParameter("Limit", RecordCount))
            .AsNoTracking()
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<BenchEntity>> FilteredQuery_Pengdows()
    {
        _filteredQuerySc.SetParameterValue("IsActive", true);
        _filteredQuerySc.SetParameterValue("MinAge", RowScalingSeed.FilteredMinAge);
        _filteredQuerySc.SetParameterValue("MaxAge", RowScalingSeed.FilteredMaxAge);
        return await _gateway.LoadListAsync(_filteredQuerySc);
    }

    [Benchmark]
    public async Task<List<DapperBenchEntity>> FilteredQuery_Dapper()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<DapperBenchEntity>(FilteredQuerySql, new
        {
            IsActive = true,
            MinAge = RowScalingSeed.FilteredMinAge,
            MaxAge = RowScalingSeed.FilteredMaxAge,
            Limit = RecordCount
        });
        return rows.ToList();
    }

    [Benchmark]
    public async Task<List<EfBenchEntity>> FilteredQuery_EntityFramework()
    {
        await using var ctx = new EfPgBenchContext(_efOptions);
        return await ctx.Benchmarks
            .FromSqlRaw(FilteredQuerySql,
                new NpgsqlParameter("IsActive", true),
                new NpgsqlParameter("MinAge", RowScalingSeed.FilteredMinAge),
                new NpgsqlParameter("MaxAge", RowScalingSeed.FilteredMaxAge),
                new NpgsqlParameter("Limit", RecordCount))
            .AsNoTracking()
            .ToListAsync();
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = _originalMatchNamesWithUnderscores;
        _readListSc?.Dispose();
        _filteredQuerySc?.Dispose();
        _pengdowsContext?.Dispose();
        _dapperDataSource?.Dispose();
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    public void Dispose() => GlobalCleanup().GetAwaiter().GetResult();

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
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("PostgreSQL container did not become ready within 30 seconds.");
    }
}
