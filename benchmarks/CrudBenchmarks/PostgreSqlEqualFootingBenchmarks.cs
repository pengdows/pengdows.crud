using System.Data;
using System.Diagnostics;
using System.Text;
using System.Threading;
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
/// Equal-footing CRUD benchmark against a real PostgreSQL instance (via Testcontainers).
///
/// Structurally identical to EqualFootingCrudBenchmarks except:
///   - Testcontainers PostgreSQL replaces SQLite shared-cache in-memory
///   - NpgsqlConnection / NpgsqlFactory / NpgsqlParameter replace SQLite equivalents
///   - PostgreSQL DDL (SERIAL, BOOLEAN, DOUBLE PRECISION)
///   - No _Native variants (SQLite int64 coercion is not a PostgreSQL concern)
///
/// The key question this answers: does the ~1.5x Pengdows/Dapper ratio hold when
/// real network I/O dominates, or does it dissolve into noise?
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class PostgreSqlEqualFootingBenchmarks : IDisposable
{
    private const int SeedRows = 1000;

    private IContainer _container = null!;
    private string _connStr = null!;

    // pengdows.crud
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<BenchEntity, int> _gateway = null!;
    private TypeMapRegistry _typeMap = null!;

    // Entity Framework
    private DbContextOptions<EfPgBenchContext> _efOptions = null!;

    // Unique ID seeds for delete / batch-create benchmarks — start high to avoid
    // colliding with SERIAL-generated IDs from Create_* benchmarks (~1000 rows seeded)
    private int _deleteIdSeed = 1_000_000;
    private int _batchIdSeed = 2_000_000;

    [Params(1, 10, 100)] public int RecordCount { get; set; }

    // ========================================================================
    // SETUP / TEARDOWN
    // ========================================================================

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new ContainerBuilder()
            .WithImage("postgres:16-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "bench")
            .WithEnvironment("POSTGRES_USER", "bench")
            .WithEnvironment("POSTGRES_DB", "benchmark")
            .WithPortBinding(5432, true)
            .Build();

        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(5432);
        _connStr =
            $"Host=localhost;Port={hostPort};Username=bench;Password=bench;Database=benchmark;" +
            "Pooling=true;Minimum Pool Size=5;Maximum Pool Size=100;Timeout=15;CommandTimeout=30;";

        await WaitForReadyAsync(_connStr);

        // Schema + seed
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

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

        // Single multi-row INSERT for speed
        var sb = new StringBuilder(
            "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES ");
        var now = DateTime.UtcNow.ToString("O");
        for (var i = 1; i <= SeedRows; i++)
        {
            if (i > 1) sb.Append(',');
            var active = (i % 2 == 0) ? "true" : "false";
            sb.Append(
                $"('Person {i}', {20 + (i % 50)}, {30000.0 + i * 100.0}, {active}, '{now}')");
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sb.ToString();
            await cmd.ExecuteNonQueryAsync();
        }

        // pengdows.crud
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<BenchEntity>();
        _pengdowsContext = new DatabaseContext(_connStr, NpgsqlFactory.Instance, _typeMap);
        _gateway = new TableGateway<BenchEntity, int>(_pengdowsContext);

        // Entity Framework
        _efOptions = new DbContextOptionsBuilder<EfPgBenchContext>()
            .UseNpgsql(_connStr)
            .Options;
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _pengdowsContext?.Dispose();
        if (_container != null) await _container.DisposeAsync();
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

    // ========================================================================
    // CREATE BENCHMARKS
    // ========================================================================

    [Benchmark]
    public async Task<int> Create_Pengdows()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            const string sql =
                "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES (@Name, @Age, @Salary, @IsActive, @CreatedAt)";
            await using var container = _pengdowsContext.CreateSqlContainer(sql);
            container.AddParameterWithValue("Name", DbType.String, $"Created {i}");
            container.AddParameterWithValue("Age", DbType.Int32, 25);
            container.AddParameterWithValue("Salary", DbType.Double, 50000.0);
            container.AddParameterWithValue("IsActive", DbType.Boolean, true);
            container.AddParameterWithValue("CreatedAt", DbType.String, DateTime.UtcNow.ToString("O"));
            count += await container.ExecuteNonQueryAsync();
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
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync();
            count += await conn.ExecuteAsync(sql, new
            {
                Name = $"Created {i}", Age = 25, Salary = 50000.0,
                IsActive = true, CreatedAt = DateTime.UtcNow.ToString("O")
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
            await using var ctx = new EfPgBenchContext(_efOptions);
            count += await ctx.Database.ExecuteSqlRawAsync(sql,
                new NpgsqlParameter("Name", $"Created {i}"),
                new NpgsqlParameter("Age", 25),
                new NpgsqlParameter("Salary", 50000.0),
                new NpgsqlParameter("IsActive", true),
                new NpgsqlParameter("CreatedAt", DateTime.UtcNow.ToString("O")));
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
            await using var container = _pengdowsContext.CreateSqlContainer();
            container.Query.Append(
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = ");
            container.Query.Append(container.MakeParameterName("Id"));
            container.AddParameterWithValue("Id", DbType.Int32, (i % SeedRows) + 1);
            result = await _gateway.LoadSingleAsync(container);
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
            await using var conn = new NpgsqlConnection(_connStr);
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
            await using var ctx = new EfPgBenchContext(_efOptions);
            result = await ctx.Benchmarks
                .FromSqlRaw(sql, new NpgsqlParameter("Id", (i % SeedRows) + 1))
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
        await using var container = _pengdowsContext.CreateSqlContainer();
        container.Query.Append(
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > ");
        container.Query.Append(container.MakeParameterName("Age"));
        container.Query.Append(" LIMIT ");
        container.Query.Append(container.MakeParameterName("Limit"));
        container.AddParameterWithValue("Age", DbType.Int32, 30);
        container.AddParameterWithValue("Limit", DbType.Int32, RecordCount);
        return await _gateway.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperBenchEntity>> ReadList_Dapper()
    {
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > @Age LIMIT @Limit";
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<DapperBenchEntity>(
            sql, new { Age = 30, Limit = RecordCount });
        return rows.ToList();
    }

    [Benchmark]
    public async Task<List<EfBenchEntity>> ReadList_EntityFramework()
    {
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > @Age LIMIT @Limit";
        await using var ctx = new EfPgBenchContext(_efOptions);
        return await ctx.Benchmarks
            .FromSqlRaw(sql,
                new NpgsqlParameter("Age", 30),
                new NpgsqlParameter("Limit", RecordCount))
            .AsNoTracking()
            .ToListAsync();
    }

    // ========================================================================
    // UPDATE BENCHMARKS
    // ========================================================================

    [Benchmark]
    public async Task<int> Update_Pengdows()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            const string sql = "UPDATE benchmark SET salary = @Salary WHERE id = @Id";
            await using var container = _pengdowsContext.CreateSqlContainer(sql);
            container.AddParameterWithValue("Salary", DbType.Double, 60000.0 + i);
            container.AddParameterWithValue("Id", DbType.Int32, (i % SeedRows) + 1);
            count += await container.ExecuteNonQueryAsync();
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
            await using var conn = new NpgsqlConnection(_connStr);
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
            await using var ctx = new EfPgBenchContext(_efOptions);
            count += await ctx.Database.ExecuteSqlRawAsync(sql,
                new NpgsqlParameter("Salary", 60000.0 + i),
                new NpgsqlParameter("Id", (i % SeedRows) + 1));
        }

        return count;
    }

    // ========================================================================
    // DELETE BENCHMARKS (insert-then-delete to avoid row depletion)
    // ========================================================================

    [Benchmark]
    public async Task<int> Delete_Pengdows()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _deleteIdSeed);
            const string insertSql =
                "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES (@Id, @Name, @Age, @Salary, @IsActive, @CreatedAt)";
            await using (var ins = _pengdowsContext.CreateSqlContainer(insertSql))
            {
                ins.AddParameterWithValue("Id", DbType.Int32, id);
                ins.AddParameterWithValue("Name", DbType.String, "ToDelete");
                ins.AddParameterWithValue("Age", DbType.Int32, 99);
                ins.AddParameterWithValue("Salary", DbType.Double, 1.0);
                ins.AddParameterWithValue("IsActive", DbType.Boolean, false);
                ins.AddParameterWithValue("CreatedAt", DbType.String, DateTime.UtcNow.ToString("O"));
                await ins.ExecuteNonQueryAsync();
            }

            const string deleteSql = "DELETE FROM benchmark WHERE id = @Id";
            await using var del = _pengdowsContext.CreateSqlContainer(deleteSql);
            del.AddParameterWithValue("Id", DbType.Int32, id);
            count += await del.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<int> Delete_Dapper()
    {
        const string insertSql =
            "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES (@Id, @Name, @Age, @Salary, @IsActive, @CreatedAt)";
        const string deleteSql = "DELETE FROM benchmark WHERE id = @Id";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _deleteIdSeed);
            {
                await using var conn = new NpgsqlConnection(_connStr);
                await conn.OpenAsync();
                await conn.ExecuteAsync(insertSql, new
                {
                    Id = id, Name = "ToDelete", Age = 99, Salary = 1.0,
                    IsActive = false, CreatedAt = DateTime.UtcNow.ToString("O")
                });
            }
            {
                await using var conn = new NpgsqlConnection(_connStr);
                await conn.OpenAsync();
                count += await conn.ExecuteAsync(deleteSql, new { Id = id });
            }
        }

        return count;
    }

    [Benchmark]
    public async Task<int> Delete_EntityFramework()
    {
        const string insertSql =
            "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES (@Id, @Name, @Age, @Salary, @IsActive, @CreatedAt)";
        const string deleteSql = "DELETE FROM benchmark WHERE id = @Id";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _deleteIdSeed);
            {
                await using var ctx = new EfPgBenchContext(_efOptions);
                await ctx.Database.ExecuteSqlRawAsync(insertSql,
                    new NpgsqlParameter("Id", id),
                    new NpgsqlParameter("Name", "ToDelete"),
                    new NpgsqlParameter("Age", 99),
                    new NpgsqlParameter("Salary", 1.0),
                    new NpgsqlParameter("IsActive", false),
                    new NpgsqlParameter("CreatedAt", DateTime.UtcNow.ToString("O")));
            }
            {
                await using var ctx = new EfPgBenchContext(_efOptions);
                await ctx.Database.ExecuteSqlRawAsync(deleteSql, new NpgsqlParameter("Id", id));
            }
        }

        return count;
    }

    // ========================================================================
    // FILTERED QUERY BENCHMARKS
    // ========================================================================

    [Benchmark]
    public async Task<List<BenchEntity>> FilteredQuery_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        container.Query.Append(
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE is_active = ");
        container.Query.Append(container.MakeParameterName("IsActive"));
        container.Query.Append(" AND age >= ");
        container.Query.Append(container.MakeParameterName("MinAge"));
        container.Query.Append(" AND age <= ");
        container.Query.Append(container.MakeParameterName("MaxAge"));
        container.Query.Append(" LIMIT ");
        container.Query.Append(container.MakeParameterName("Limit"));
        container.AddParameterWithValue("IsActive", DbType.Boolean, true);
        container.AddParameterWithValue("MinAge", DbType.Int32, 25);
        container.AddParameterWithValue("MaxAge", DbType.Int32, 45);
        container.AddParameterWithValue("Limit", DbType.Int32, RecordCount);
        return await _gateway.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperBenchEntity>> FilteredQuery_Dapper()
    {
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE is_active = @IsActive AND age >= @MinAge AND age <= @MaxAge LIMIT @Limit";
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<DapperBenchEntity>(
            sql, new { IsActive = true, MinAge = 25, MaxAge = 45, Limit = RecordCount });
        return rows.ToList();
    }

    [Benchmark]
    public async Task<List<EfBenchEntity>> FilteredQuery_EntityFramework()
    {
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE is_active = @IsActive AND age >= @MinAge AND age <= @MaxAge LIMIT @Limit";
        await using var ctx = new EfPgBenchContext(_efOptions);
        return await ctx.Benchmarks
            .FromSqlRaw(sql,
                new NpgsqlParameter("IsActive", true),
                new NpgsqlParameter("MinAge", 25),
                new NpgsqlParameter("MaxAge", 45),
                new NpgsqlParameter("Limit", RecordCount))
            .AsNoTracking()
            .ToListAsync();
    }

    // ========================================================================
    // AGGREGATE BENCHMARKS
    // PostgreSQL AVG on DOUBLE PRECISION returns double precision — no cast needed.
    // Uses TRUE instead of 1 for BOOLEAN column comparison.
    // ========================================================================

    [Benchmark]
    public async Task<double> Aggregate_Pengdows()
    {
        double result = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var container = _pengdowsContext.CreateSqlContainer(
                "SELECT AVG(salary) FROM benchmark WHERE is_active = TRUE");
            result = await container.ExecuteScalarOrNullAsync<double>();
        }

        return result;
    }

    [Benchmark]
    public async Task<double> Aggregate_Dapper()
    {
        double result = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync();
            result = await conn.ExecuteScalarAsync<double>(
                "SELECT AVG(salary) FROM benchmark WHERE is_active = TRUE");
        }

        return result;
    }

    [Benchmark]
    public async Task<double> Aggregate_EntityFramework()
    {
        double result = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var ctx = new EfPgBenchContext(_efOptions);
            var conn = ctx.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT AVG(salary) FROM benchmark WHERE is_active = TRUE";
            var scalar = await cmd.ExecuteScalarAsync();
            result = Convert.ToDouble(scalar);
        }

        return result;
    }

    // ========================================================================
    // BATCH CREATE BENCHMARKS (one connection per row — shows per-op overhead)
    // ========================================================================

    [Benchmark]
    public async Task<int> BatchCreate_Pengdows()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _batchIdSeed);
            const string sql =
                "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES (@Id, @Name, @Age, @Salary, @IsActive, @CreatedAt)";
            await using var container = _pengdowsContext.CreateSqlContainer(sql);
            container.AddParameterWithValue("Id", DbType.Int32, id);
            container.AddParameterWithValue("Name", DbType.String, $"Batch {id}");
            container.AddParameterWithValue("Age", DbType.Int32, 30);
            container.AddParameterWithValue("Salary", DbType.Double, 55000.0);
            container.AddParameterWithValue("IsActive", DbType.Boolean, true);
            container.AddParameterWithValue("CreatedAt", DbType.String, DateTime.UtcNow.ToString("O"));
            count += await container.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<int> BatchCreate_Dapper()
    {
        const string sql =
            "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES (@Id, @Name, @Age, @Salary, @IsActive, @CreatedAt)";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _batchIdSeed);
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync();
            count += await conn.ExecuteAsync(sql, new
            {
                Id = id, Name = $"Batch {id}", Age = 30, Salary = 55000.0,
                IsActive = true, CreatedAt = DateTime.UtcNow.ToString("O")
            });
        }

        return count;
    }

    [Benchmark]
    public async Task<int> BatchCreate_EntityFramework()
    {
        const string sql =
            "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES (@Id, @Name, @Age, @Salary, @IsActive, @CreatedAt)";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _batchIdSeed);
            await using var ctx = new EfPgBenchContext(_efOptions);
            count += await ctx.Database.ExecuteSqlRawAsync(sql,
                new NpgsqlParameter("Id", id),
                new NpgsqlParameter("Name", $"Batch {id}"),
                new NpgsqlParameter("Age", 30),
                new NpgsqlParameter("Salary", 55000.0),
                new NpgsqlParameter("IsActive", true),
                new NpgsqlParameter("CreatedAt", DateTime.UtcNow.ToString("O")));
        }

        return count;
    }

    // ========================================================================
    // BULK CREATE (multi-row VALUES — single round-trip for N rows)
    // ========================================================================

    [Benchmark]
    public async Task<int> BulkCreate_Pengdows()
    {
        var entities = new List<BenchEntity>(RecordCount);
        for (var i = 0; i < RecordCount; i++)
        {
            entities.Add(new BenchEntity
            {
                Name = $"Bulk {i}", Age = 30, Salary = 55000.0,
                IsActive = true, CreatedAt = DateTime.UtcNow.ToString("O")
            });
        }

        return await _gateway.BatchCreateAsync(entities, _pengdowsContext);
    }

    [Benchmark]
    public async Task<int> BulkCreate_Dapper()
    {
        if (RecordCount == 0) return 0;

        var parameters = new DynamicParameters();
        var valueClauses = new string[RecordCount];
        for (var i = 0; i < RecordCount; i++)
        {
            valueClauses[i] = $"(@Name{i}, @Age{i}, @Salary{i}, @IsActive{i}, @CreatedAt{i})";
            parameters.Add($"Name{i}", $"Bulk {i}");
            parameters.Add($"Age{i}", 30);
            parameters.Add($"Salary{i}", 55000.0);
            parameters.Add($"IsActive{i}", true);
            parameters.Add($"CreatedAt{i}", DateTime.UtcNow.ToString("O"));
        }

        var sql = "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES " +
                  string.Join(", ", valueClauses);
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        return await conn.ExecuteAsync(sql, parameters);
    }

    [Benchmark]
    public async Task<int> BulkCreate_EntityFramework()
    {
        if (RecordCount == 0) return 0;

        var efParams = new List<NpgsqlParameter>(RecordCount * 5);
        var valueClauses = new string[RecordCount];
        for (var i = 0; i < RecordCount; i++)
        {
            valueClauses[i] = $"(@Name{i}, @Age{i}, @Salary{i}, @IsActive{i}, @CreatedAt{i})";
            efParams.Add(new NpgsqlParameter($"Name{i}", $"Bulk {i}"));
            efParams.Add(new NpgsqlParameter($"Age{i}", 30));
            efParams.Add(new NpgsqlParameter($"Salary{i}", 55000.0));
            efParams.Add(new NpgsqlParameter($"IsActive{i}", true));
            efParams.Add(new NpgsqlParameter($"CreatedAt{i}", DateTime.UtcNow.ToString("O")));
        }

        var sql = "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES " +
                  string.Join(", ", valueClauses);
        await using var ctx = new EfPgBenchContext(_efOptions);
        return await ctx.Database.ExecuteSqlRawAsync(sql, efParams);
    }

    // ========================================================================
    // BULK vs LOOP comparison — pengdows only
    // ========================================================================

    [Benchmark]
    public async Task<int> BulkVsLoop_SingleInserts_Pengdows()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var entity = new BenchEntity
            {
                Name = $"Loop {i}", Age = 30, Salary = 55000.0,
                IsActive = true, CreatedAt = DateTime.UtcNow.ToString("O")
            };
            var sc = _gateway.BuildCreate(entity, _pengdowsContext);
            count += await sc.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<int> BulkVsLoop_BatchCreate_Pengdows()
    {
        var entities = new List<BenchEntity>(RecordCount);
        for (var i = 0; i < RecordCount; i++)
        {
            entities.Add(new BenchEntity
            {
                Name = $"Batch {i}", Age = 30, Salary = 55000.0,
                IsActive = true, CreatedAt = DateTime.UtcNow.ToString("O")
            });
        }

        return await _gateway.BatchCreateAsync(entities, _pengdowsContext);
    }

    // ========================================================================
    // BREAKDOWN: BUILD vs EXECUTE timing
    // ========================================================================

    [Benchmark]
    public async Task<(long BuildTicks, long ExecuteTicks)> Breakdown_BuildVsExecute_Pengdows()
    {
        var sw = new Stopwatch();
        long buildTicks = 0, executeTicks = 0;

        for (var i = 0; i < RecordCount; i++)
        {
            sw.Restart();
            var container = _pengdowsContext.CreateSqlContainer();
            container.Query.Append(
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = ");
            container.Query.Append(container.MakeParameterName("Id"));
            container.AddParameterWithValue("Id", DbType.Int32, (i % SeedRows) + 1);
            sw.Stop();
            buildTicks += sw.ElapsedTicks;

            sw.Restart();
            await _gateway.LoadSingleAsync(container);
            sw.Stop();
            executeTicks += sw.ElapsedTicks;

            await container.DisposeAsync();
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
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync();
            await conn.QueryFirstOrDefaultAsync<DapperBenchEntity>(sql, param);
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
            var param = new NpgsqlParameter("Id", (i % SeedRows) + 1);
            sw.Stop();
            buildTicks += sw.ElapsedTicks;

            sw.Restart();
            await using var ctx = new EfPgBenchContext(_efOptions);
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
    // For PostgreSQL this measures: pool acquire + wire protocol round-trip + pool release.
    // Does NOT include TCP connect (Npgsql pools keep connections open).
    // Compare to SQLite (~34 µs) to quantify wire protocol overhead.
    // ========================================================================

    [Benchmark]
    public async Task<long> ConnectionHoldTime_Pengdows()
    {
        var sw = Stopwatch.StartNew();
        await using var container = _pengdowsContext.CreateSqlContainer();
        container.Query.Append(
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = ");
        container.Query.Append(container.MakeParameterName("Id"));
        container.AddParameterWithValue("Id", DbType.Int32, 1);
        await _gateway.LoadSingleAsync(container);
        sw.Stop();
        return sw.ElapsedTicks;
    }

    [Benchmark]
    public async Task<long> ConnectionHoldTime_Dapper()
    {
        var sw = Stopwatch.StartNew();
        await using var conn = new NpgsqlConnection(_connStr);
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
        await using var ctx = new EfPgBenchContext(_efOptions);
        await ctx.Benchmarks
            .FromSqlRaw(
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id",
                new NpgsqlParameter("Id", 1))
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

    public class EfPgBenchContext : DbContext
    {
        public EfPgBenchContext(DbContextOptions<EfPgBenchContext> options) : base(options) { }

        public DbSet<EfBenchEntity> Benchmarks { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfBenchEntity>(entity =>
            {
                entity.ToTable("benchmark");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Age).HasColumnName("age");
                entity.Property(e => e.Salary).HasColumnName("salary");
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            });
        }
    }
}
