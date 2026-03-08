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
/// pengdows.crud side uses the full TableGateway API:
///   - BuildCreate for INSERT (framework generates dialect-correct INSERT, type-safe params)
///   - BuildRetrieve for keyed reads (reused container + SetParameterValue)
///   - BuildBaseRetrieve + WrapObjectName for custom queries (reused + SetParameterValue)
///   - BuildUpdateAsync for full UPDATE (reused container + SetParameterValue on changed fields)
///   - BuildDelete for DELETE (reused container + SetParameterValue)
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
    private TableGateway<BenchEntityNative, long> _gatewayNative = null!;
    private TypeMapRegistry _typeMap = null!;

    // Entity Framework options (DbContext created per operation)
    private DbContextOptions<EfBenchContext> _efOptions = null!;

    // Counter for unique IDs in delete benchmarks
    private int _deleteIdSeed = 100_000;

    // Reusable ISqlContainer fields — built once in GlobalSetup using TableGateway Build* methods.
    // Sequential benchmark loops reuse the same container via SetParameterValue; no SQL
    // re-generation and no allocation per iteration.
    private ISqlContainer _readSingleSc = null!;
    private ISqlContainer _readSingleNativeSc = null!;
    private ISqlContainer _readListSc = null!;
    private ISqlContainer _readListNativeSc = null!;
    private ISqlContainer _filteredQuerySc = null!;
    private ISqlContainer _updateSc = null!;
    private ISqlContainer _deleteInsertSc = null!;
    private ISqlContainer _deleteSc = null!;
    private ISqlContainer _aggregateSc = null!;

    private bool _originalMatchNamesWithUnderscores;

    [Params(1, 100)] public int RecordCount { get; set; }

    // ========================================================================
    // SETUP / TEARDOWN
    // ========================================================================

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _originalMatchNamesWithUnderscores = DefaultTypeMap.MatchNamesWithUnderscores;
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        // Sentinel connection — keeps the in-memory database alive
        _sentinel = new SqliteConnection(ConnStr);
        _sentinel.Open();

        // Create schema via sentinel
        await using (var cmd = _sentinel.CreateCommand())
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
        await using (var tx = _sentinel.BeginTransaction())
        {
            await using var cmd = _sentinel.CreateCommand();
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
        _typeMap.Register<BenchEntityNative>();
        _pengdowsContext = new DatabaseContext(ConnStr, SqliteFactory.Instance, _typeMap);
        _gateway = new TableGateway<BenchEntity, int>(_pengdowsContext);
        _gatewayNative = new TableGateway<BenchEntityNative, long>(_pengdowsContext);

        // Entity Framework options — DbContext created per operation
        _efOptions = new DbContextOptionsBuilder<EfBenchContext>()
            .UseSqlite(ConnStr)
            .Options;

        // ---- Build reusable containers once ----

        // ReadSingle — keyed by id collection; loop calls SetParameterValue("w0", id) (scalar)
        _readSingleSc = _gateway.BuildRetrieve(new[] { 1 });
        _readSingleNativeSc = _gatewayNative.BuildRetrieve(new[] { 1L });

        // ReadList — BuildBaseRetrieve + custom WHERE age > @Age LIMIT @Limit
        _readListSc = _gateway.BuildBaseRetrieve("b");
        _readListSc.Query.Append($" WHERE {_pengdowsContext.WrapObjectName("b.age")} > ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Age"));
        _readListSc.AddParameterWithValue("Age", DbType.Int32, 0);
        _readListSc.Query.Append(" LIMIT ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Limit"));
        _readListSc.AddParameterWithValue("Limit", DbType.Int32, RecordCount);

        _readListNativeSc = _gatewayNative.BuildBaseRetrieve("b");
        _readListNativeSc.Query.Append($" WHERE {_pengdowsContext.WrapObjectName("b.age")} > ");
        _readListNativeSc.Query.Append(_readListNativeSc.MakeParameterName("Age"));
        _readListNativeSc.AddParameterWithValue("Age", DbType.Int64, 0L);
        _readListNativeSc.Query.Append(" LIMIT ");
        _readListNativeSc.Query.Append(_readListNativeSc.MakeParameterName("Limit"));
        _readListNativeSc.AddParameterWithValue("Limit", DbType.Int32, RecordCount);

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
        _filteredQuerySc.Query.Append(" LIMIT ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("Limit"));
        _filteredQuerySc.AddParameterWithValue("Limit", DbType.Int32, RecordCount);

        // Update — full entity UPDATE; loop sets SetParameterValue("s2", salary) + ("k0", id)
        // Column SET order: name=s0, age=s1, salary=s2, is_active=s3, created_at=s4; WHERE id=k0
        _updateSc = await _gateway.BuildUpdateAsync(new BenchEntity
        {
            Id = 1, Name = "Updated", Age = 25,
            Salary = 50000.0, IsActive = true, CreatedAt = DateTime.UtcNow.ToString("O")
        });

        // Delete insert side — explicit id required since [Id(false)] would omit it from BuildCreate.
        // Reuse via SetParameterValue("Id", id) each iteration; other fields stay fixed.
        _deleteInsertSc = _pengdowsContext.CreateSqlContainer();
        _deleteInsertSc.Query.Append("INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES (");
        _deleteInsertSc.Query.Append(_deleteInsertSc.MakeParameterName("Id"));
        _deleteInsertSc.Query.Append(", ");
        _deleteInsertSc.Query.Append(_deleteInsertSc.MakeParameterName("Name"));
        _deleteInsertSc.Query.Append(", ");
        _deleteInsertSc.Query.Append(_deleteInsertSc.MakeParameterName("Age"));
        _deleteInsertSc.Query.Append(", ");
        _deleteInsertSc.Query.Append(_deleteInsertSc.MakeParameterName("Salary"));
        _deleteInsertSc.Query.Append(", ");
        _deleteInsertSc.Query.Append(_deleteInsertSc.MakeParameterName("IsActive"));
        _deleteInsertSc.Query.Append(", ");
        _deleteInsertSc.Query.Append(_deleteInsertSc.MakeParameterName("CreatedAt"));
        _deleteInsertSc.Query.Append(")");
        _deleteInsertSc.AddParameterWithValue("Id", DbType.Int32, 0);
        _deleteInsertSc.AddParameterWithValue("Name", DbType.String, "ToDelete");
        _deleteInsertSc.AddParameterWithValue("Age", DbType.Int32, 99);
        _deleteInsertSc.AddParameterWithValue("Salary", DbType.Double, 1.0);
        _deleteInsertSc.AddParameterWithValue("IsActive", DbType.Boolean, false);
        _deleteInsertSc.AddParameterWithValue("CreatedAt", DbType.String, DateTime.UtcNow.ToString("O"));

        // Delete — BuildDelete; loop sets SetParameterValue("k0", id)
        _deleteSc = _gateway.BuildDelete(0);

        // Aggregate — no variable params; same container reused each iteration
        _aggregateSc = _pengdowsContext.CreateSqlContainer(
            "SELECT AVG(salary) FROM benchmark WHERE is_active = 1");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = _originalMatchNamesWithUnderscores;
        if (_pengdowsContext != null)
        {
            BenchmarkMetricsWriter.Write(
                nameof(EqualFootingCrudBenchmarks),
                _pengdowsContext,
                $"RecordCount={RecordCount}");
        }

        _readSingleSc?.Dispose();
        _readSingleNativeSc?.Dispose();
        _readListSc?.Dispose();
        _readListNativeSc?.Dispose();
        _filteredQuerySc?.Dispose();
        _updateSc?.Dispose();
        _deleteInsertSc?.Dispose();
        _deleteSc?.Dispose();
        _aggregateSc?.Dispose();
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
                Name = $"Created {i}", Age = 25, Salary = 50000.0,
                IsActive = true, CreatedAt = DateTime.UtcNow.ToString("O")
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
            await using var conn = new SqliteConnection(ConnStr);
            await conn.OpenAsync();
            result = await conn.QueryFirstOrDefaultAsync<DapperBenchEntity>(
                sql, new { Id = (i % SeedRows) + 1 });
        }

        return result;
    }

    /// <summary>
    /// Same query as ReadSingle_Pengdows but using BenchEntityNative (long/long/long) so
    /// SQLite's native int64 return values require zero coercion. The delta vs
    /// ReadSingle_Pengdows isolates the int64→int32 and int64→bool coercion cost.
    /// </summary>
    [Benchmark]
    public async Task<BenchEntityNative?> ReadSingle_Pengdows_Native()
    {
        BenchEntityNative? result = null;
        for (var i = 0; i < RecordCount; i++)
        {
            _readSingleNativeSc.SetParameterValue("w0", (long)((i % SeedRows) + 1));
            result = await _gatewayNative.LoadSingleAsync(_readSingleNativeSc);
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
        _readListSc.SetParameterValue("Age", 30);
        return await _gateway.LoadListAsync(_readListSc);
    }

    /// <summary>
    /// Same query as ReadList_Pengdows but using BenchEntityNative — zero coercion.
    /// </summary>
    [Benchmark]
    public async Task<List<BenchEntityNative>> ReadList_Pengdows_Native()
    {
        _readListNativeSc.SetParameterValue("Age", 30L);
        return await _gatewayNative.LoadListAsync(_readListNativeSc);
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

    /// <summary>
    /// Uses reusable containers for both the insert and delete steps.
    /// Insert uses a manually-built container (explicit id required; [Id(false)] means BuildCreate omits it).
    /// Delete uses BuildDelete with SetParameterValue("k0", id).
    /// </summary>
    [Benchmark]
    public async Task<int> Delete_Pengdows()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _deleteIdSeed);

            _deleteInsertSc.SetParameterValue("Id", id);
            await _deleteInsertSc.ExecuteNonQueryAsync();

            _deleteSc.SetParameterValue("k0", id);
            count += await _deleteSc.ExecuteNonQueryAsync();
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
        _filteredQuerySc.SetParameterValue("IsActive", true);
        _filteredQuerySc.SetParameterValue("MinAge", 25);
        _filteredQuerySc.SetParameterValue("MaxAge", 45);
        return await _gateway.LoadListAsync(_filteredQuerySc);
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
    // BREAKDOWN: BUILD vs EXECUTE timing
    // Isolates how long BuildRetrieve takes vs actual I/O execution.
    // Shows the framework's SQL-generation cost is negligible compared to DB round-trip.
    // ========================================================================

    [Benchmark]
    public async Task<(long BuildTicks, long ExecuteTicks)> Breakdown_BuildVsExecute_Pengdows()
    {
        var sw = new Stopwatch();
        long buildTicks = 0;
        long executeTicks = 0;

        for (var i = 0; i < RecordCount; i++)
        {
            // Measure the full Build* cost: SQL generation + parameter creation
            sw.Restart();
            var sc = _gateway.BuildRetrieve(new[] { (i % SeedRows) + 1 });
            sw.Stop();
            buildTicks += sw.ElapsedTicks;

            // Measure execute phase: connection open/close + wire + mapping
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
        _readSingleSc.SetParameterValue("w0", 1);
        await _gateway.LoadSingleAsync(_readSingleSc);
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
    // ENTITY: pengdows.crud
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

    // ========================================================================
    // ENTITY: pengdows.crud — SQLite native types (no coercion)
    // id/age/is_active declared as long so SQLite's int64 return values
    // map directly without int64→int32 or int64→bool conversion.
    // ========================================================================

    [Table("benchmark")]
    public class BenchEntityNative
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Column("age", DbType.Int64)] public long Age { get; set; }

        [Column("salary", DbType.Double)] public double Salary { get; set; }

        [Column("is_active", DbType.Int64)] public long IsActive { get; set; }

        [Column("created_at", DbType.String)] public string CreatedAt { get; set; } = string.Empty;
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
