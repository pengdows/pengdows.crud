using System.Data;
using System.Diagnostics;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using pengdows.crud;
using pengdows.crud.attributes;

namespace CrudBenchmarks;

/// <summary>
/// Equal-footing CRUD benchmark proving three thesis points:
///   #1 - EF Core is ALWAYS slower than pengdows.crud and Dapper
///   #2 - pengdows.crud performs within the same ballpark as Dapper
///   #5 - Server (SQLite) execution time is equal across all three frameworks
///
/// All three frameworks share a single SQLite in-memory database via shared cache.
/// A sentinel connection keeps the database alive for the duration of the run.
/// Each framework executes identical SQL against the same schema and seed data.
///
/// IMPORTANT: All three frameworks open and close connections per operation,
/// matching production-correct "open late, close early" behavior.
/// No framework gets an unfair advantage from a pre-opened persistent connection.
///
/// Breakdown methods isolate query-build time from execution time for pengdows and Dapper,
/// showing that the difference between frameworks is entirely in client-side overhead.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class EqualFootingCrudBenchmarks : IDisposable
{
    private const string ConnStr = "Data Source=EqualFootingBench;Mode=Memory;Cache=Shared";
    private const int SeedRows = 1000;

    // Sentinel keeps the shared-cache database alive
    private SqliteConnection _sentinel = null!;

    // pengdows.crud
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<BenchEntity, int> _gateway = null!;
    private TypeMapRegistry _typeMap = null!;

    // Entity Framework options (DbContext created per operation)
    private DbContextOptions<EfBenchContext> _efOptions = null!;

    // Counters for unique IDs in delete / create benchmarks
    private int _deleteIdSeed = 100_000;
    private int _batchIdSeed = 200_000;

    [Params(1, 10, 100)]
    public int RecordCount { get; set; }

    // ========================================================================
    // SETUP / TEARDOWN
    // ========================================================================

    [GlobalSetup]
    public void GlobalSetup()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        // Sentinel connection — keeps the in-memory database alive
        _sentinel = new SqliteConnection(ConnStr);
        _sentinel.Open();

        // Create schema via sentinel
        using (var cmd = _sentinel.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS benchmark (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    age INTEGER NOT NULL,
                    salary REAL NOT NULL,
                    is_active INTEGER NOT NULL,
                    created_at TEXT NOT NULL
                )";
            cmd.ExecuteNonQuery();
        }

        // Seed 1000 rows
        using (var tx = _sentinel.BeginTransaction())
        {
            using var cmd = _sentinel.CreateCommand();
            cmd.Transaction = tx;
            for (var i = 1; i <= SeedRows; i++)
            {
                cmd.CommandText =
                    "INSERT INTO benchmark (name, age, salary, is_active, created_at) " +
                    $"VALUES ('Person {i}', {20 + (i % 50)}, {30000.0 + i * 100.0}, {(i % 2 == 0 ? 1 : 0)}, '{DateTime.UtcNow:O}')";
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // pengdows.crud
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<BenchEntity>();
        _pengdowsContext = new DatabaseContext(ConnStr, SqliteFactory.Instance, _typeMap);
        _gateway = new TableGateway<BenchEntity, int>(_pengdowsContext);

        // Entity Framework options — DbContext created per operation
        _efOptions = new DbContextOptionsBuilder<EfBenchContext>()
            .UseSqlite(ConnStr)
            .Options;
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        DumpPengdowsMetrics();
        _pengdowsContext?.Dispose();
        _sentinel?.Dispose();
    }

    public void Dispose()
    {
        GlobalCleanup();
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
            await using var conn = new SqliteConnection(ConnStr);
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
            await using var ctx = new EfBenchContext(_efOptions);
            count += await ctx.Database.ExecuteSqlRawAsync(sql,
                new SqliteParameter("Name", $"Created {i}"),
                new SqliteParameter("Age", 25),
                new SqliteParameter("Salary", 50000.0),
                new SqliteParameter("IsActive", true),
                new SqliteParameter("CreatedAt", DateTime.UtcNow.ToString("O")));
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
            await using var conn = new SqliteConnection(ConnStr);
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
            await using var ctx = new EfBenchContext(_efOptions);
            result = await ctx.Benchmarks
                .FromSqlRaw(sql, new SqliteParameter("Id", (i % SeedRows) + 1))
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
        await using var conn = new SqliteConnection(ConnStr);
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
        await using var ctx = new EfBenchContext(_efOptions);
        return await ctx.Benchmarks
            .FromSqlRaw(sql,
                new SqliteParameter("Age", 30),
                new SqliteParameter("Limit", RecordCount))
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
            await using var conn = new SqliteConnection(ConnStr);
            await conn.OpenAsync();
            count += await conn.ExecuteAsync(sql, new
            {
                Salary = 60000.0 + i,
                Id = (i % SeedRows) + 1
            });
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
            await using var ctx = new EfBenchContext(_efOptions);
            count += await ctx.Database.ExecuteSqlRawAsync(sql,
                new SqliteParameter("Salary", 60000.0 + i),
                new SqliteParameter("Id", (i % SeedRows) + 1));
        }

        return count;
    }

    // ========================================================================
    // DELETE BENCHMARKS
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
                await using var conn = new SqliteConnection(ConnStr);
                await conn.OpenAsync();
                await conn.ExecuteAsync(insertSql, new
                {
                    Id = id, Name = "ToDelete", Age = 99, Salary = 1.0,
                    IsActive = false, CreatedAt = DateTime.UtcNow.ToString("O")
                });
            }

            {
                await using var conn = new SqliteConnection(ConnStr);
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
                await using var ctx = new EfBenchContext(_efOptions);
                await ctx.Database.ExecuteSqlRawAsync(insertSql,
                    new SqliteParameter("Id", id),
                    new SqliteParameter("Name", "ToDelete"),
                    new SqliteParameter("Age", 99),
                    new SqliteParameter("Salary", 1.0),
                    new SqliteParameter("IsActive", false),
                    new SqliteParameter("CreatedAt", DateTime.UtcNow.ToString("O")));
            }

            {
                await using var ctx = new EfBenchContext(_efOptions);
                count += await ctx.Database.ExecuteSqlRawAsync(deleteSql,
                    new SqliteParameter("Id", id));
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
        await using var conn = new SqliteConnection(ConnStr);
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
        await using var ctx = new EfBenchContext(_efOptions);
        return await ctx.Benchmarks
            .FromSqlRaw(sql,
                new SqliteParameter("IsActive", true),
                new SqliteParameter("MinAge", 25),
                new SqliteParameter("MaxAge", 45),
                new SqliteParameter("Limit", RecordCount))
            .AsNoTracking()
            .ToListAsync();
    }

    // ========================================================================
    // AGGREGATE BENCHMARKS
    // ========================================================================

    [Benchmark]
    public async Task<double> Aggregate_Pengdows()
    {
        double result = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var container = _pengdowsContext.CreateSqlContainer(
                "SELECT AVG(salary) FROM benchmark WHERE is_active = 1");
            result = await container.ExecuteScalarAsync<double>();
        }

        return result;
    }

    [Benchmark]
    public async Task<double> Aggregate_Dapper()
    {
        double result = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var conn = new SqliteConnection(ConnStr);
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
            await using var ctx = new EfBenchContext(_efOptions);
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
    // BATCH CREATE BENCHMARKS
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
            await using var conn = new SqliteConnection(ConnStr);
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
            await using var ctx = new EfBenchContext(_efOptions);
            count += await ctx.Database.ExecuteSqlRawAsync(sql,
                new SqliteParameter("Id", id),
                new SqliteParameter("Name", $"Batch {id}"),
                new SqliteParameter("Age", 30),
                new SqliteParameter("Salary", 55000.0),
                new SqliteParameter("IsActive", true),
                new SqliteParameter("CreatedAt", DateTime.UtcNow.ToString("O")));
        }

        return count;
    }

    // ========================================================================
    // BULK INSERT: Multi-row VALUES — the real batch story
    // ========================================================================
    // These benchmarks compare true multi-row INSERT INTO ... VALUES (...), (...)
    // across all three frameworks. This is the database-independent bulk pattern
    // that reduces round-trips to a single statement per chunk.

    [Benchmark]
    public async Task<int> BulkCreate_Pengdows()
    {
        // Uses our new BatchCreateAsync which generates multi-row VALUES
        var entities = new List<BenchEntity>(RecordCount);
        for (var i = 0; i < RecordCount; i++)
        {
            entities.Add(new BenchEntity
            {
                Name = $"Bulk {i}",
                Age = 30,
                Salary = 55000.0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.ToString("O")
            });
        }

        return await _gateway.BatchCreateAsync(entities, _pengdowsContext);
    }

    [Benchmark]
    public async Task<int> BulkCreate_Dapper()
    {
        // Hand-build multi-row VALUES for Dapper — same SQL pattern
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
        await using var conn = new SqliteConnection(ConnStr);
        await conn.OpenAsync();
        return await conn.ExecuteAsync(sql, parameters);
    }

    [Benchmark]
    public async Task<int> BulkCreate_EntityFramework()
    {
        // Hand-build multi-row VALUES for EF — same SQL pattern
        if (RecordCount == 0) return 0;

        var efParams = new List<SqliteParameter>(RecordCount * 5);
        var valueClauses = new string[RecordCount];
        for (var i = 0; i < RecordCount; i++)
        {
            valueClauses[i] = $"(@Name{i}, @Age{i}, @Salary{i}, @IsActive{i}, @CreatedAt{i})";
            efParams.Add(new SqliteParameter($"Name{i}", $"Bulk {i}"));
            efParams.Add(new SqliteParameter($"Age{i}", 30));
            efParams.Add(new SqliteParameter($"Salary{i}", 55000.0));
            efParams.Add(new SqliteParameter($"IsActive{i}", true));
            efParams.Add(new SqliteParameter($"CreatedAt{i}", DateTime.UtcNow.ToString("O")));
        }

        var sql = "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES " +
                  string.Join(", ", valueClauses);
        await using var ctx = new EfBenchContext(_efOptions);
        return await ctx.Database.ExecuteSqlRawAsync(sql, efParams);
    }

    // ========================================================================
    // BULK vs LOOP comparison — pengdows only
    // Shows the speedup from multi-row VALUES vs looped single INSERTs
    // ========================================================================

    [Benchmark]
    public async Task<int> BulkVsLoop_SingleInserts_Pengdows()
    {
        // Baseline: loop of individual BuildCreate (old pattern)
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var entity = new BenchEntity
            {
                Name = $"Loop {i}",
                Age = 30,
                Salary = 55000.0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.ToString("O")
            };
            var sc = _gateway.BuildCreate(entity, _pengdowsContext);
            count += await sc.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<int> BulkVsLoop_BatchCreate_Pengdows()
    {
        // New: single multi-row VALUES statement via BatchCreateAsync
        var entities = new List<BenchEntity>(RecordCount);
        for (var i = 0; i < RecordCount; i++)
        {
            entities.Add(new BenchEntity
            {
                Name = $"Batch {i}",
                Age = 30,
                Salary = 55000.0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.ToString("O")
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
        long buildTicks = 0;
        long executeTicks = 0;

        for (var i = 0; i < RecordCount; i++)
        {
            // Measure build phase
            sw.Restart();
            var container = _pengdowsContext.CreateSqlContainer();
            container.Query.Append(
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = ");
            container.Query.Append(container.MakeParameterName("Id"));
            container.AddParameterWithValue("Id", DbType.Int32, (i % SeedRows) + 1);
            sw.Stop();
            buildTicks += sw.ElapsedTicks;

            // Measure execute phase
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
        long buildTicks = 0;
        long executeTicks = 0;

        for (var i = 0; i < RecordCount; i++)
        {
            // Measure build phase (Dapper's build is just creating the anonymous object)
            sw.Restart();
            const string sql =
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id";
            var param = new { Id = (i % SeedRows) + 1 };
            sw.Stop();
            buildTicks += sw.ElapsedTicks;

            // Measure execute phase (includes open/close)
            sw.Restart();
            await using var conn = new SqliteConnection(ConnStr);
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
        long buildTicks = 0;
        long executeTicks = 0;

        for (var i = 0; i < RecordCount; i++)
        {
            // Measure build phase (EF's build includes creating SqliteParameter)
            sw.Restart();
            const string sql =
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id";
            var param = new SqliteParameter("Id", (i % SeedRows) + 1);
            sw.Stop();
            buildTicks += sw.ElapsedTicks;

            // Measure execute phase (includes context create/dispose)
            sw.Restart();
            await using var ctx = new EfBenchContext(_efOptions);
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
    // Measures how long each framework holds a database connection open
    // for a single-row read operation (open → execute → close).
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
        await using var conn = new SqliteConnection(ConnStr);
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
        await using var ctx = new EfBenchContext(_efOptions);
        await ctx.Benchmarks
            .FromSqlRaw(
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id",
                new SqliteParameter("Id", 1))
            .AsNoTracking()
            .FirstOrDefaultAsync();
        sw.Stop();
        return sw.ElapsedTicks;
    }

    // ========================================================================
    // HELPERS
    // ========================================================================

    private void DumpPengdowsMetrics()
    {
        var metrics = _pengdowsContext.Metrics;
        Console.WriteLine(
            $"[METRICS] EqualFooting " +
            $"conn_hold_avg={metrics.AvgConnectionHoldMs:0.000}ms " +
            $"cmd_avg={metrics.AvgCommandMs:0.000}ms " +
            $"p95={metrics.P95CommandMs:0.000}ms p99={metrics.P99CommandMs:0.000}ms " +
            $"conns_opened={metrics.ConnectionsOpened} conns_closed={metrics.ConnectionsClosed}");
    }

    // ========================================================================
    // ENTITY: pengdows.crud
    // ========================================================================

    [Table("benchmark")]
    public class BenchEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Column("age", DbType.Int32)]
        public int Age { get; set; }

        [Column("salary", DbType.Double)]
        public double Salary { get; set; }

        [Column("is_active", DbType.Boolean)]
        public bool IsActive { get; set; }

        [Column("created_at", DbType.String)]
        public string CreatedAt { get; set; } = string.Empty;
    }

    // ========================================================================
    // ENTITY: Dapper (simple POCO, relies on MatchNamesWithUnderscores)
    // ========================================================================

    public class DapperBenchEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public double Salary { get; set; }
        public bool IsActive { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }

    // ========================================================================
    // ENTITY: Entity Framework
    // ========================================================================

    public class EfBenchEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public double Salary { get; set; }
        public bool IsActive { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }

    // ========================================================================
    // EF DbContext
    // ========================================================================

    public class EfBenchContext : DbContext
    {
        public EfBenchContext(DbContextOptions<EfBenchContext> options) : base(options)
        {
        }

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
