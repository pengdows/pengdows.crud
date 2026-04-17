using System.Data;
using System.Diagnostics;
using System.Text;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using pengdows.crud;
using pengdows.crud.attributes;

namespace CrudBenchmarks;

/// <summary>
/// Equal-footing CRUD benchmark against a real SQL Server instance (via Testcontainers).
///
/// Structurally identical to PostgreSqlEqualFootingBenchmarks except:
///   - Testcontainers SQL Server 2022 replaces PostgreSQL
///   - SqlConnection / SqlClientFactory / SqlParameter replace Npgsql equivalents
///   - SQL Server DDL (INT IDENTITY, NVARCHAR, FLOAT, BIT)
///   - LIMIT → ORDER BY id OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY
///   - Aggregate uses WHERE is_active = 1 (BIT comparison)
///   - DeleteInsertCycle omitted — SQL Server IDENTITY columns cannot accept explicit values
///     without SET IDENTITY_INSERT ON, which is connection-scoped and incompatible with
///     per-operation pool connections used here.
///   - No equal-footing auto-prepare setup — SQL Server plan cache is fully server-side;
///     no client configuration needed.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SqlServerEqualFootingBenchmarks : IDisposable
{
    private const int SeedRows = 1000;

    private IContainer _container = null!;
    private string _connStr = null!;

    // pengdows.crud
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<BenchEntity, int> _gateway = null!;
    private TypeMapRegistry _typeMap = null!;

    // Entity Framework
    private DbContextOptions<EfSqlBenchContext> _efOptions = null!;

    private bool _originalMatchNamesWithUnderscores;

    // Reusable ISqlContainer fields — built once in GlobalSetup using TableGateway Build* methods.
    // Sequential benchmark loops reuse the same container via SetParameterValue; no SQL
    // re-generation and no allocation per iteration.
    private ISqlContainer _readSingleSc = null!;
    private ISqlContainer _readListSc = null!;
    private ISqlContainer _filteredQuerySc = null!;
    private ISqlContainer _updateSc = null!;
    private ISqlContainer _deleteSc = null!;
    private ISqlContainer _aggregateSc = null!;

    [Params(1, 10, 100)] public int RecordCount { get; set; }

    // ========================================================================
    // SETUP / TEARDOWN
    // ========================================================================

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _originalMatchNamesWithUnderscores = DefaultTypeMap.MatchNamesWithUnderscores;
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", "Benchmark_P@ss1")
            .WithEnvironment("MSSQL_PID", "Developer")
            .WithPortBinding(1433, true)
            .Build();

        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(1433);
        var masterConnStr =
            $"Server=localhost,{hostPort};Database=master;User Id=sa;Password=Benchmark_P@ss1;TrustServerCertificate=True;";
        _connStr =
            $"Server=localhost,{hostPort};Database=benchmark;User Id=sa;Password=Benchmark_P@ss1;TrustServerCertificate=True;";

        await WaitForReadyAsync(masterConnStr);

        // Create the benchmark database from master
        await using (var masterConn = new SqlConnection(masterConnStr))
        {
            await masterConn.OpenAsync();
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = "IF DB_ID('benchmark') IS NULL CREATE DATABASE benchmark";
            await cmd.ExecuteNonQueryAsync();
        }

        // Schema + seed
        await using (var conn = new SqlConnection(_connStr))
        {
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

            // Single multi-row INSERT for speed
            var sb = new StringBuilder(
                "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES ");
            var now = DateTime.UtcNow.ToString("O");
            for (var i = 1; i <= SeedRows; i++)
            {
                if (i > 1) sb.Append(',');
                var active = (i % 2 == 0) ? "1" : "0";
                sb.Append(
                    $"(N'Person {i}', {20 + (i % 50)}, {30000.0 + i * 100.0}, {active}, N'{now}')");
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sb.ToString();
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // pengdows.crud
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<BenchEntity>();
        _pengdowsContext = new DatabaseContext(_connStr, SqlClientFactory.Instance, _typeMap);
        _gateway = new TableGateway<BenchEntity, int>(_pengdowsContext);

        // Entity Framework
        _efOptions = new DbContextOptionsBuilder<EfSqlBenchContext>()
            .UseSqlServer(_connStr)
            .Options;

        // ---- Build reusable containers once ----

        // ReadSingle — keyed by id collection; loop calls SetParameterValue("w0", id) (scalar)
        _readSingleSc = _gateway.BuildRetrieve(new[] { 1 });

        // ReadList — BuildBaseRetrieve + custom WHERE age > @Age ORDER BY id OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY
        _readListSc = _gateway.BuildBaseRetrieve("b");
        _readListSc.Query.Append($" WHERE {_pengdowsContext.WrapObjectName("b.age")} > ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Age"));
        _readListSc.AddParameterWithValue("Age", DbType.Int32, 0);
        _readListSc.Query.Append($" ORDER BY {_pengdowsContext.WrapObjectName("b.id")}");
        _readListSc.Query.Append(" OFFSET 0 ROWS FETCH NEXT ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Limit"));
        _readListSc.Query.Append(" ROWS ONLY");
        _readListSc.AddParameterWithValue("Limit", DbType.Int32, RecordCount);

        // FilteredQuery — BuildBaseRetrieve + multi-condition WHERE
        _filteredQuerySc = _gateway.BuildBaseRetrieve("b");
        _filteredQuerySc.Query.Append($" WHERE {_pengdowsContext.WrapObjectName("b.is_active")} = ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("IsActive"));
        _filteredQuerySc.AddParameterWithValue("IsActive", DbType.Boolean, true);
        _filteredQuerySc.Query.Append($" AND {_pengdowsContext.WrapObjectName("b.age")} >= ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("MinAge"));
        _filteredQuerySc.AddParameterWithValue("MinAge", DbType.Int32, 0);
        _filteredQuerySc.Query.Append($" AND {_pengdowsContext.WrapObjectName("b.age")} <= ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("MaxAge"));
        _filteredQuerySc.AddParameterWithValue("MaxAge", DbType.Int32, 0);
        _filteredQuerySc.Query.Append($" ORDER BY {_pengdowsContext.WrapObjectName("b.id")}");
        _filteredQuerySc.Query.Append(" OFFSET 0 ROWS FETCH NEXT ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("Limit"));
        _filteredQuerySc.Query.Append(" ROWS ONLY");
        _filteredQuerySc.AddParameterWithValue("Limit", DbType.Int32, RecordCount);

        // Update — full entity UPDATE; loop sets SetParameterValue("s2", salary) + ("k0", id)
        // Column SET order: name=s0, age=s1, salary=s2, is_active=s3, created_at=s4; WHERE id=k0
        _updateSc = await _gateway.BuildUpdateAsync(new BenchEntity
        {
            Id = 1,
            Name = "Updated",
            Age = 25,
            Salary = 50000.0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.ToString("O")
        });

        // Delete — BuildDelete; loop sets SetParameterValue("k0", id)
        _deleteSc = _gateway.BuildDelete(0);

        // Aggregate — no variable params; same container reused each iteration
        // BIT column comparison: is_active = 1
        _aggregateSc = _pengdowsContext.CreateSqlContainer(
            "SELECT AVG(salary) FROM benchmark WHERE is_active = 1");

        // ---- Pre-warm EF/Dapper reflection caches ----
        // SQL Server plan cache is fully server-side — no client auto-prepare configuration needed.
        // 2 warmup iterations are sufficient to populate EF/Dapper reflection metadata caches.
        const int prewarmCount = 2;
        await PreWarmFrameworkCachesAsync(prewarmCount);
    }

    private async Task PreWarmFrameworkCachesAsync(int prewarmCount)
    {
        const string createSql =
            "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES (@Name, @Age, @Salary, @IsActive, @CreatedAt)";
        const string readSingleSql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id";
        const string readListSql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > @Age ORDER BY id OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY";
        const string updateSql = "UPDATE benchmark SET salary = @Salary WHERE id = @Id";
        const string deleteSql = "DELETE FROM benchmark WHERE id = @Id";
        const string filteredQuerySql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE is_active = @IsActive AND age >= @MinAge AND age <= @MaxAge ORDER BY id OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY";
        const string aggregateSql = "SELECT AVG(salary) FROM benchmark WHERE is_active = 1";

        for (var pw = 0; pw < prewarmCount; pw++)
        {
            var createdAt = DateTime.UtcNow.ToString("O");

            // ReadSingle
            _readSingleSc.SetParameterValue("w0", (pw % SeedRows) + 1);
            await _gateway.LoadSingleAsync(_readSingleSc);

            // ReadList
            _readListSc.SetParameterValue("Age", 0);
            await _gateway.LoadListAsync(_readListSc);

            // FilteredQuery
            _filteredQuerySc.SetParameterValue("IsActive", true);
            _filteredQuerySc.SetParameterValue("MinAge", 20);
            _filteredQuerySc.SetParameterValue("MaxAge", 60);
            await _gateway.LoadListAsync(_filteredQuerySc);

            // Update
            _updateSc.SetParameterValue("s2", 60000.0 + pw);
            _updateSc.SetParameterValue("k0", (pw % SeedRows) + 1);
            await _updateSc.ExecuteNonQueryAsync();

            // DeleteOnly (inside a rollback so we keep the row)
            await using (var tx = await _pengdowsContext.BeginTransactionAsync())
            await using (var deleteOnlySc = _deleteSc.Clone(tx))
            {
                deleteOnlySc.SetParameterValue("k0", (pw % SeedRows) + 1);
                await deleteOnlySc.ExecuteNonQueryAsync();
                tx.Rollback();
            }

            // Aggregate
            await _aggregateSc.ExecuteScalarOrNullAsync<double>();

            // Create
            var warmupEntity = new BenchEntity
            {
                Name = $"Warmup {pw}",
                Age = 25,
                Salary = 50000.0,
                IsActive = true,
                CreatedAt = createdAt
            };
            await using var warmupCreateSc = _gateway.BuildCreate(warmupEntity);
            await warmupCreateSc.ExecuteNonQueryAsync();

            // Dapper warmup
            await using (var conn = new SqlConnection(_connStr))
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync(createSql, new
                {
                    Name = $"Warmup {pw}",
                    Age = 25,
                    Salary = 50000.0,
                    IsActive = true,
                    CreatedAt = createdAt
                });
                await conn.QueryFirstOrDefaultAsync<DapperBenchEntity>(readSingleSql,
                    new { Id = (pw % SeedRows) + 1 });
                await conn.QueryAsync<DapperBenchEntity>(readListSql, new { Age = 0, Limit = RecordCount });
                await conn.QueryAsync<DapperBenchEntity>(filteredQuerySql,
                    new { IsActive = true, MinAge = 20, MaxAge = 60, Limit = RecordCount });
                await conn.ExecuteAsync(updateSql, new { Salary = 60000.0 + pw, Id = (pw % SeedRows) + 1 });
                await conn.ExecuteScalarAsync<double>(aggregateSql);
            }

            // Dapper DeleteOnly warmup (inside rollback)
            await using (var deleteConn = new SqlConnection(_connStr))
            {
                await deleteConn.OpenAsync();
                await using var tx = await deleteConn.BeginTransactionAsync();
                await deleteConn.ExecuteAsync(deleteSql, new { Id = (pw % SeedRows) + 1 }, tx);
                await tx.RollbackAsync();
            }

            // EF warmup
            await using (var efCtx = new EfSqlBenchContext(_efOptions))
            {
                await efCtx.Database.ExecuteSqlRawAsync(createSql,
                    new SqlParameter("Name", $"Warmup {pw}"),
                    new SqlParameter("Age", 25),
                    new SqlParameter("Salary", 50000.0),
                    new SqlParameter("IsActive", true),
                    new SqlParameter("CreatedAt", createdAt));
                _ = await efCtx.Benchmarks
                    .FromSqlRaw(readSingleSql, new SqlParameter("Id", (pw % SeedRows) + 1))
                    .AsNoTracking()
                    .FirstOrDefaultAsync();
                _ = await efCtx.Benchmarks
                    .FromSqlRaw(readListSql,
                        new SqlParameter("Age", 0),
                        new SqlParameter("Limit", RecordCount))
                    .AsNoTracking()
                    .ToListAsync();
                _ = await efCtx.Benchmarks
                    .FromSqlRaw(filteredQuerySql,
                        new SqlParameter("IsActive", true),
                        new SqlParameter("MinAge", 20),
                        new SqlParameter("MaxAge", 60),
                        new SqlParameter("Limit", RecordCount))
                    .AsNoTracking()
                    .ToListAsync();
                await efCtx.Database.ExecuteSqlRawAsync(updateSql,
                    new SqlParameter("Salary", 60000.0 + pw),
                    new SqlParameter("Id", (pw % SeedRows) + 1));
                await efCtx.Database.SqlQueryRaw<double>(aggregateSql).FirstAsync();
            }

            // EF DeleteOnly warmup (inside rollback)
            await using (var efDeleteCtx = new EfSqlBenchContext(_efOptions))
            {
                await using var tx = await efDeleteCtx.Database.BeginTransactionAsync();
                await efDeleteCtx.Database.ExecuteSqlRawAsync(deleteSql,
                    new SqlParameter("Id", (pw % SeedRows) + 1));
                await tx.RollbackAsync();
            }
        }
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = _originalMatchNamesWithUnderscores;

        _readSingleSc?.Dispose();
        _readListSc?.Dispose();
        _filteredQuerySc?.Dispose();
        _updateSc?.Dispose();
        _deleteSc?.Dispose();
        _aggregateSc?.Dispose();

        if (_pengdowsContext != null)
        {
            BenchmarkMetricsWriter.Write(nameof(SqlServerEqualFootingBenchmarks), _pengdowsContext,
                $"RecordCount={RecordCount}");
        }

        _pengdowsContext?.Dispose();
        if (_container != null) await _container.DisposeAsync();
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

    // ========================================================================
    // CREATE BENCHMARKS
    // ========================================================================

    /// <summary>
    /// Uses BuildCreate — framework generates the dialect-correct INSERT with type-safe parameters.
    /// Entity data changes each iteration so a new container is built per iteration.
    /// Dapper and EF Core use equivalent per-iteration construction.
    /// </summary>
    [Benchmark]
    public async Task<int> Create_Pengdows()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var entity = new BenchEntity
            {
                Name = $"Created {i}",
                Age = 25,
                Salary = 50000.0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.ToString("O")
            };
            await using var sc = _gateway.BuildCreate(entity);
            count += await sc.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<int> Create_Dapper()
    {
        const string sql =
            "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES (@Name, @Age, @Salary, @IsActive, @CreatedAt)";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();
            count += await conn.ExecuteAsync(sql, new
            {
                Name = $"Created {i}",
                Age = 25,
                Salary = 50000.0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.ToString("O")
            });
        }

        return count;
    }

    [Benchmark]
    public async Task<int> Create_EntityFramework()
    {
        const string sql =
            "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES (@Name, @Age, @Salary, @IsActive, @CreatedAt)";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var ctx = new EfSqlBenchContext(_efOptions);
            count += await ctx.Database.ExecuteSqlRawAsync(sql,
                new SqlParameter("Name", $"Created {i}"),
                new SqlParameter("Age", 25),
                new SqlParameter("Salary", 50000.0),
                new SqlParameter("IsActive", true),
                new SqlParameter("CreatedAt", DateTime.UtcNow.ToString("O")));
        }

        return count;
    }

    // ========================================================================
    // READ SINGLE BENCHMARKS
    // ========================================================================

    [Benchmark(Baseline = true)]
    public async Task<BenchEntity?> ReadSingle_Pengdows()
    {
        BenchEntity? result = null;
        for (var i = 0; i < RecordCount; i++)
        {
            _readSingleSc.SetParameterValue("w0", (i % SeedRows) + 1);
            result = await _gateway.LoadSingleAsync(_readSingleSc);
        }

        return result;
    }

    [Benchmark]
    public async Task<DapperBenchEntity?> ReadSingle_Dapper()
    {
        DapperBenchEntity? result = null;
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id";
        for (var i = 0; i < RecordCount; i++)
        {
            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();
            result = await conn.QueryFirstOrDefaultAsync<DapperBenchEntity>(
                sql, new { Id = (i % SeedRows) + 1 });
        }

        return result;
    }

    [Benchmark]
    public async Task<EfBenchEntity?> ReadSingle_EntityFramework()
    {
        EfBenchEntity? result = null;
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id";
        for (var i = 0; i < RecordCount; i++)
        {
            await using var ctx = new EfSqlBenchContext(_efOptions);
            result = await ctx.Benchmarks
                .FromSqlRaw(sql, new SqlParameter("Id", (i % SeedRows) + 1))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }

        return result;
    }

    // ========================================================================
    // READ LIST BENCHMARKS
    // ========================================================================

    [Benchmark]
    public async Task<List<BenchEntity>> ReadList_Pengdows()
    {
        _readListSc.SetParameterValue("Age", 30);
        return await _gateway.LoadListAsync(_readListSc);
    }

    [Benchmark]
    public async Task<List<DapperBenchEntity>> ReadList_Dapper()
    {
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > @Age ORDER BY id OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY";
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<DapperBenchEntity>(
            sql, new { Age = 30, Limit = RecordCount });
        return rows.ToList();
    }

    [Benchmark]
    public async Task<List<EfBenchEntity>> ReadList_EntityFramework()
    {
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > @Age ORDER BY id OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY";
        await using var ctx = new EfSqlBenchContext(_efOptions);
        return await ctx.Benchmarks
            .FromSqlRaw(sql,
                new SqlParameter("Age", 30),
                new SqlParameter("Limit", RecordCount))
            .AsNoTracking()
            .ToListAsync();
    }

    // ========================================================================
    // UPDATE BENCHMARKS
    // ========================================================================

    /// <summary>
    /// Uses BuildUpdateAsync — framework generates a full UPDATE for all columns.
    /// Reuses the pre-built container; only salary (s2) and id (k0) are varied.
    /// </summary>
    [Benchmark]
    public async Task<int> Update_Pengdows()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            _updateSc.SetParameterValue("s2", 60000.0 + i);
            _updateSc.SetParameterValue("k0", (i % SeedRows) + 1);
            count += await _updateSc.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<int> Update_Dapper()
    {
        const string sql = "UPDATE benchmark SET salary = @Salary WHERE id = @Id";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();
            count += await conn.ExecuteAsync(sql,
                new { Salary = 60000.0 + i, Id = (i % SeedRows) + 1 });
        }

        return count;
    }

    [Benchmark]
    public async Task<int> Update_EntityFramework()
    {
        const string sql = "UPDATE benchmark SET salary = @Salary WHERE id = @Id";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var ctx = new EfSqlBenchContext(_efOptions);
            count += await ctx.Database.ExecuteSqlRawAsync(sql,
                new SqlParameter("Salary", 60000.0 + i),
                new SqlParameter("Id", (i % SeedRows) + 1));
        }

        return count;
    }

    // ========================================================================
    // DELETE BENCHMARKS
    // ========================================================================

    /// <summary>
    /// Measures a true DELETE against an existing row.
    /// Each operation runs inside a transaction and rolls back so the seeded row remains
    /// available for subsequent iterations and BenchmarkDotNet invocations.
    /// </summary>
    [Benchmark]
    public async Task<int> DeleteOnly_Pengdows()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var tx = await _pengdowsContext.BeginTransactionAsync();
            await using var deleteSc = _deleteSc.Clone(tx);
            deleteSc.SetParameterValue("k0", (i % SeedRows) + 1);
            count += await deleteSc.ExecuteNonQueryAsync();
            tx.Rollback();
        }

        return count;
    }

    [Benchmark]
    public async Task<int> DeleteOnly_Dapper()
    {
        const string deleteSql = "DELETE FROM benchmark WHERE id = @Id";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            count += await conn.ExecuteAsync(deleteSql, new { Id = (i % SeedRows) + 1 }, tx);
            await tx.RollbackAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<int> DeleteOnly_EntityFramework()
    {
        const string deleteSql = "DELETE FROM benchmark WHERE id = @Id";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var ctx = new EfSqlBenchContext(_efOptions);
            await using var tx = await ctx.Database.BeginTransactionAsync();
            count += await ctx.Database.ExecuteSqlRawAsync(deleteSql,
                new SqlParameter("Id", (i % SeedRows) + 1));
            await tx.RollbackAsync();
        }

        return count;
    }

    // ========================================================================
    // FILTERED QUERY BENCHMARKS
    // ========================================================================

    [Benchmark]
    public async Task<List<BenchEntity>> FilteredQuery_Pengdows()
    {
        _filteredQuerySc.SetParameterValue("IsActive", true);
        _filteredQuerySc.SetParameterValue("MinAge", 25);
        _filteredQuerySc.SetParameterValue("MaxAge", 45);
        return await _gateway.LoadListAsync(_filteredQuerySc);
    }

    [Benchmark]
    public async Task<List<DapperBenchEntity>> FilteredQuery_Dapper()
    {
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE is_active = @IsActive AND age >= @MinAge AND age <= @MaxAge ORDER BY id OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY";
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<DapperBenchEntity>(
            sql, new { IsActive = true, MinAge = 25, MaxAge = 45, Limit = RecordCount });
        return rows.ToList();
    }

    [Benchmark]
    public async Task<List<EfBenchEntity>> FilteredQuery_EntityFramework()
    {
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE is_active = @IsActive AND age >= @MinAge AND age <= @MaxAge ORDER BY id OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY";
        await using var ctx = new EfSqlBenchContext(_efOptions);
        return await ctx.Benchmarks
            .FromSqlRaw(sql,
                new SqlParameter("IsActive", true),
                new SqlParameter("MinAge", 25),
                new SqlParameter("MaxAge", 45),
                new SqlParameter("Limit", RecordCount))
            .AsNoTracking()
            .ToListAsync();
    }

    // ========================================================================
    // AGGREGATE BENCHMARKS
    // SQL Server AVG on FLOAT returns float — no cast needed.
    // Uses is_active = 1 for BIT column comparison.
    // ========================================================================

    [Benchmark]
    public async Task<double> Aggregate_Pengdows()
    {
        double result = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            result = await _aggregateSc.ExecuteScalarOrNullAsync<double>();
        }

        return result;
    }

    [Benchmark]
    public async Task<double> Aggregate_Dapper()
    {
        double result = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();
            result = await conn.ExecuteScalarAsync<double>(
                "SELECT AVG(salary) FROM benchmark WHERE is_active = 1");
        }

        return result;
    }

    [Benchmark]
    public async Task<double> Aggregate_EntityFramework()
    {
        double result = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var ctx = new EfSqlBenchContext(_efOptions);
            var conn = ctx.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT AVG(salary) FROM benchmark WHERE is_active = 1";
            var scalar = await cmd.ExecuteScalarAsync();
            result = Convert.ToDouble(scalar);
        }

        return result;
    }

    // ========================================================================
    // BREAKDOWN: BUILD vs EXECUTE timing
    // Isolates how long BuildRetrieve takes vs actual I/O execution.
    // Shows the framework's SQL-generation cost is negligible compared to DB round-trip.
    // ========================================================================

    [Benchmark]
    public async Task<(long BuildTicks, long ExecuteTicks)> Breakdown_BuildVsExecute_Pengdows()
    {
        var sw = new Stopwatch();
        long buildTicks = 0, executeTicks = 0;

        for (var i = 0; i < RecordCount; i++)
        {
            sw.Restart();
            var sc = _gateway.BuildRetrieve(new[] { (i % SeedRows) + 1 });
            sw.Stop();
            buildTicks += sw.ElapsedTicks;

            sw.Restart();
            await _gateway.LoadSingleAsync(sc);
            sw.Stop();
            executeTicks += sw.ElapsedTicks;

            await sc.DisposeAsync();
        }

        return (buildTicks, executeTicks);
    }

    [Benchmark]
    public async Task<(long BuildTicks, long ExecuteTicks)> Breakdown_BuildVsExecute_Dapper()
    {
        var sw = new Stopwatch();
        long buildTicks = 0, executeTicks = 0;

        for (var i = 0; i < RecordCount; i++)
        {
            sw.Restart();
            const string sql =
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id";
            var param = new { Id = (i % SeedRows) + 1 };
            sw.Stop();
            buildTicks += sw.ElapsedTicks;

            sw.Restart();
            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();
            await conn.QueryFirstOrDefaultAsync<DapperBenchEntity>(sql, param);
            await conn.CloseAsync();
            sw.Stop();
            executeTicks += sw.ElapsedTicks;
        }

        return (buildTicks, executeTicks);
    }

    [Benchmark]
    public async Task<(long BuildTicks, long ExecuteTicks)> Breakdown_BuildVsExecute_EntityFramework()
    {
        var sw = new Stopwatch();
        long buildTicks = 0, executeTicks = 0;

        for (var i = 0; i < RecordCount; i++)
        {
            sw.Restart();
            const string sql =
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id";
            var param = new SqlParameter("Id", (i % SeedRows) + 1);
            sw.Stop();
            buildTicks += sw.ElapsedTicks;

            sw.Restart();
            await using var ctx = new EfSqlBenchContext(_efOptions);
            await ctx.Benchmarks
                .FromSqlRaw(sql, param)
                .AsNoTracking()
                .FirstOrDefaultAsync();
            sw.Stop();
            executeTicks += sw.ElapsedTicks;
        }

        return (buildTicks, executeTicks);
    }

    // ========================================================================
    // CONNECTION HOLD TIME
    // Measures: pool acquire + wire protocol round-trip + pool release.
    // ========================================================================

    [Benchmark]
    public async Task<long> ConnectionHoldTime_Pengdows()
    {
        var sw = Stopwatch.StartNew();
        _readSingleSc.SetParameterValue("w0", 1);
        await _gateway.LoadSingleAsync(_readSingleSc);
        sw.Stop();
        return sw.ElapsedTicks;
    }

    [Benchmark]
    public async Task<long> ConnectionHoldTime_Dapper()
    {
        var sw = Stopwatch.StartNew();
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();
        await conn.QueryFirstOrDefaultAsync<DapperBenchEntity>(
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id",
            new { Id = 1 });
        sw.Stop();
        return sw.ElapsedTicks;
    }

    [Benchmark]
    public async Task<long> ConnectionHoldTime_EntityFramework()
    {
        var sw = Stopwatch.StartNew();
        await using var ctx = new EfSqlBenchContext(_efOptions);
        await ctx.Benchmarks
            .FromSqlRaw(
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id",
                new SqlParameter("Id", 1))
            .AsNoTracking()
            .FirstOrDefaultAsync();
        sw.Stop();
        return sw.ElapsedTicks;
    }

    // ========================================================================
    // ENTITIES
    // ========================================================================

    [Table("benchmark")]
    public class BenchEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
        [Column("age", DbType.Int32)] public int Age { get; set; }
        [Column("salary", DbType.Double)] public double Salary { get; set; }
        [Column("is_active", DbType.Boolean)] public bool IsActive { get; set; }
        [Column("created_at", DbType.String)] public string CreatedAt { get; set; } = string.Empty;
    }

    public class DapperBenchEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public double Salary { get; set; }
        public bool IsActive { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }

    public class EfBenchEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public double Salary { get; set; }
        public bool IsActive { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }

    public class EfSqlBenchContext : DbContext
    {
        public EfSqlBenchContext(DbContextOptions<EfSqlBenchContext> options) : base(options)
        {
        }

        public DbSet<EfBenchEntity> Benchmarks { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfBenchEntity>(entity =>
            {
                entity.ToTable("benchmark");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Age).HasColumnName("age");
                entity.Property(e => e.Salary).HasColumnName("salary");
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            });
        }
    }
}
