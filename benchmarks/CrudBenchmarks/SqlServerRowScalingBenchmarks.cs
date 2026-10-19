using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using pengdows.crud;
using BenchEntity = CrudBenchmarks.SqlServerEqualFootingBenchmarks.BenchEntity;
using DapperBenchEntity = CrudBenchmarks.SqlServerEqualFootingBenchmarks.DapperBenchEntity;
using EfBenchEntity = CrudBenchmarks.SqlServerEqualFootingBenchmarks.EfBenchEntity;
using EfSqlBenchContext = CrudBenchmarks.SqlServerEqualFootingBenchmarks.EfSqlBenchContext;

namespace CrudBenchmarks;

/// <summary>
/// The SQL Server counterpart of <see cref="PostgreSqlRowScalingBenchmarks"/>: <see cref="SqlServerEqualFootingBenchmarks"/>'
/// ReadList and FilteredQuery arms (same SQL, a pooled <see cref="SqlConnection"/> opened per call) at 1 to 10,000 rows.
/// Opt-in: <c>--filter "*SqlServerRowScaling*"</c>.
/// </summary>
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SqlServerRowScalingBenchmarks : IDisposable
{
    internal const string ReadListSql =
        "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > @Age ORDER BY id OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY";

    internal const string FilteredQuerySql =
        "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE is_active = @IsActive AND age >= @MinAge AND age <= @MaxAge ORDER BY id OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY";

    private const string SaPassword = "Benchmark_P@ss1";

    private IContainer _container = null!;
    private string _connStr = null!;
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<BenchEntity, int> _gateway = null!;
    private DbContextOptions<EfSqlBenchContext> _efOptions = null!;
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
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", SaPassword)
            .WithEnvironment("MSSQL_PID", "Developer")
            .WithPortBinding(BenchmarkHostPort.ReserveBelowEphemeralRange(), 1433)
            .Build();
        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(1433);
        var masterConnStr =
            $"Server=localhost,{hostPort};Database=master;User Id=sa;Password={SaPassword};TrustServerCertificate=True;";
        _connStr =
            $"Server=localhost,{hostPort};Database=benchmark;User Id=sa;Password={SaPassword};TrustServerCertificate=True;";
        await WaitForReadyAsync(masterConnStr);
        await SeedAsync(masterConnStr);

        var typeMap = new TypeMapRegistry();
        typeMap.Register<BenchEntity>();
        _pengdowsContext = new DatabaseContext(_connStr, SqlClientFactory.Instance, typeMap);
        _gateway = new TableGateway<BenchEntity, int>(_pengdowsContext);
        _efOptions = new DbContextOptionsBuilder<EfSqlBenchContext>().UseSqlServer(_connStr).Options;

        _readListSc = _gateway.BuildBaseRetrieve("b");
        _readListSc.Query.Append($" WHERE {_pengdowsContext.WrapObjectName("b.age")} > ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Age"));
        _readListSc.AddParameterWithValue("Age", DbType.Int32, RowScalingSeed.ReadListMinAgeExclusive);
        _readListSc.Query.Append($" ORDER BY {_pengdowsContext.WrapObjectName("b.id")}");
        _readListSc.Query.Append(" OFFSET 0 ROWS FETCH NEXT ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Limit"));
        _readListSc.Query.Append(" ROWS ONLY");
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
        _filteredQuerySc.Query.Append($" ORDER BY {_pengdowsContext.WrapObjectName("b.id")}");
        _filteredQuerySc.Query.Append(" OFFSET 0 ROWS FETCH NEXT ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("Limit"));
        _filteredQuerySc.Query.Append(" ROWS ONLY");
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

    private async Task SeedAsync(string masterConnStr)
    {
        await using (var master = new SqlConnection(masterConnStr))
        {
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            create.CommandText = "IF DB_ID('benchmark') IS NULL CREATE DATABASE benchmark";
            await create.ExecuteNonQueryAsync();
        }

        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                IF OBJECT_ID('benchmark', 'U') IS NULL
                CREATE TABLE benchmark (
                    id          INT IDENTITY(1,1) PRIMARY KEY,
                    name        NVARCHAR(255) NOT NULL,
                    age         INT NOT NULL,
                    salary      FLOAT NOT NULL,
                    is_active   BIT NOT NULL,
                    created_at  NVARCHAR(50) NOT NULL
                )";
            await cmd.ExecuteNonQueryAsync();
        }

        var now = DateTime.UtcNow.ToString("O");
        foreach (var insert in RowScalingSeed.InsertStatements(RowScalingSeed.Rows, i =>
                     $"(N'Person {i}', {RowScalingSeed.Age(i)}, {30000.0 + i * 100.0}, " +
                     $"{(RowScalingSeed.IsActive(i) ? "1" : "0")}, N'{now}')"))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = insert;
            await cmd.ExecuteNonQueryAsync();
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
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<DapperBenchEntity>(ReadListSql,
            new { Age = RowScalingSeed.ReadListMinAgeExclusive, Limit = RecordCount });
        return rows.ToList();
    }

    [Benchmark]
    public async Task<List<EfBenchEntity>> ReadList_EntityFramework()
    {
        await using var ctx = new EfSqlBenchContext(_efOptions);
        return await ctx.Benchmarks
            .FromSqlRaw(ReadListSql,
                new SqlParameter("Age", RowScalingSeed.ReadListMinAgeExclusive),
                new SqlParameter("Limit", RecordCount))
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
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();
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
        await using var ctx = new EfSqlBenchContext(_efOptions);
        return await ctx.Benchmarks
            .FromSqlRaw(FilteredQuerySql,
                new SqlParameter("IsActive", true),
                new SqlParameter("MinAge", RowScalingSeed.FilteredMinAge),
                new SqlParameter("MaxAge", RowScalingSeed.FilteredMaxAge),
                new SqlParameter("Limit", RecordCount))
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
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    public void Dispose() => GlobalCleanup().GetAwaiter().GetResult();

    private static async Task WaitForReadyAsync(string connStr)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                await using var c = new SqlConnection(connStr);
                await c.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("SQL Server container did not become ready within 60 seconds.");
    }
}
