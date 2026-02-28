using System.Data;
using System.Diagnostics;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Dapper;
using DuckDB.NET.Data;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

/// <summary>
/// Equal-footing CRUD benchmark against DuckDB (file-based, in-process).
///
/// Structurally mirrors EqualFootingCrudBenchmarks (SQLite) except:
///   - DuckDB file-based database instead of SQLite shared-cache in-memory
///   - No EF Core (no DuckDB EF Core provider available) — Pengdows vs Dapper only
///   - DuckDB native types: INTEGER → int32, BOOLEAN → bool, DOUBLE → double,
///     TIMESTAMP → DateTime — zero coercion unlike SQLite's int64/TEXT returns
///   - [Id(true)] throughout: DuckDB INTEGER PRIMARY KEY has no AUTOINCREMENT;
///     all benchmark operations supply explicit IDs via atomic counters
///   - LIMIT values are inlined (DuckDB does not support named parameters in LIMIT)
///
/// All operations open and close connections per call on both frameworks,
/// matching the equal-footing methodology of EqualFootingCrudBenchmarks.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class DuckDbEqualFootingBenchmarks : IDisposable
{
    private const int SeedRows = 1000;

    private string _dbFile = null!;

    // pengdows.crud — Standard mode (connection-per-op)
    private DatabaseContext _pengdowsCtx = null!;
    private TableGateway<DuckBenchEntity, int> _gateway = null!;
    private TypeMapRegistry _typeMap = null!;

    // Explicit ID ranges — seed occupies 1..1000; ranges below avoid collision
    private int _createIdSeed = 10_000;
    private int _deleteIdSeed = 100_000;


    private bool _originalMatchNamesWithUnderscores;

    [Params(1, 10, 100)] public int RecordCount { get; set; }

    // ========================================================================
    // SETUP / TEARDOWN
    // ========================================================================

    [GlobalSetup]
    public void GlobalSetup()
    {
        _originalMatchNamesWithUnderscores = DefaultTypeMap.MatchNamesWithUnderscores;
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _dbFile = Path.Combine(
            Path.GetTempPath(),
            $"pengdows_duckdb_eqft_{Guid.NewGuid():N}.duckdb");

        using (var seedConn = new DuckDBConnection($"Data Source={_dbFile}"))
        {
            seedConn.Open();

            seedConn.Execute(@"
                CREATE TABLE benchmark (
                    id         INTEGER    PRIMARY KEY,
                    name       VARCHAR    NOT NULL,
                    age        INTEGER    NOT NULL,
                    salary     DOUBLE     NOT NULL,
                    is_active  BOOLEAN    NOT NULL,
                    created_at TIMESTAMP  NOT NULL
                )");

            // Bulk-insert 1000 seed rows via a single multi-row VALUES statement
            var rows = Enumerable.Range(1, SeedRows).Select(i =>
                $"({i}, 'Person {i}', {20 + i % 50}, {30000.0 + i * 100.0:F1}, " +
                $"{(i % 2 == 0 ? "true" : "false")}, " +
                $"'{DateTime.UtcNow.AddDays(-i):yyyy-MM-dd HH:mm:ss}')");

            seedConn.Execute(
                "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES " +
                string.Join(",", rows));
        }

        _typeMap = new TypeMapRegistry();
        _typeMap.Register<DuckBenchEntity>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={_dbFile}",
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsCtx = new DatabaseContext(cfg, DuckDBClientFactory.Instance, null, _typeMap);
        _gateway = new TableGateway<DuckBenchEntity, int>(_pengdowsCtx);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = _originalMatchNamesWithUnderscores;

        if (_pengdowsCtx != null)
        {
            BenchmarkMetricsWriter.Write(
                nameof(DuckDbEqualFootingBenchmarks),
                _pengdowsCtx,
                $"RecordCount={RecordCount}");
        }

        _pengdowsCtx?.Dispose();

        foreach (var f in new[] { _dbFile, _dbFile + ".wal" })
        {
            if (File.Exists(f))
            {
                try { File.Delete(f); }
                catch { /* best-effort */ }
            }
        }
    }

    public void Dispose() => GlobalCleanup();

    // ========================================================================
    // CREATE BENCHMARKS
    // ========================================================================

    [Benchmark]
    public async Task<int> Create_Pengdows()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _createIdSeed);
            await using var container = _pengdowsCtx.CreateSqlContainer();
            container.Query.Append(
                "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES (");
            container.Query.Append(container.MakeParameterName("Id"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("Name"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("Age"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("Salary"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("IsActive"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("CreatedAt"));
            container.Query.Append(')');
            container.AddParameterWithValue("Id", DbType.Int32, id);
            container.AddParameterWithValue("Name", DbType.String, $"Created {id}");
            container.AddParameterWithValue("Age", DbType.Int32, 25);
            container.AddParameterWithValue("Salary", DbType.Double, 50000.0);
            container.AddParameterWithValue("IsActive", DbType.Boolean, true);
            container.AddParameterWithValue("CreatedAt", DbType.DateTime, DateTime.UtcNow);
            count += await container.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<int> Create_Dapper()
    {
        const string sql =
            "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES ($Id, $Name, $Age, $Salary, $IsActive, $CreatedAt)";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _createIdSeed);
            await using var conn = new DuckDBConnection($"Data Source={_dbFile}");
            await conn.OpenAsync();
            count += await conn.ExecuteAsync(sql, new
            {
                Id = id,
                Name = $"Created {id}",
                Age = 25,
                Salary = 50000.0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        return count;
    }

    // ========================================================================
    // READ SINGLE BENCHMARKS
    // ========================================================================

    [Benchmark(Baseline = true)]
    public async Task<DuckBenchEntity?> ReadSingle_Pengdows()
    {
        DuckBenchEntity? result = null;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var container = _pengdowsCtx.CreateSqlContainer();
            container.Query.Append(
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = ");
            container.Query.Append(container.MakeParameterName("Id"));
            container.AddParameterWithValue("Id", DbType.Int32, (i % SeedRows) + 1);
            result = await _gateway.LoadSingleAsync(container);
        }

        return result;
    }

    [Benchmark]
    public async Task<DuckBenchEntity?> ReadSingle_Dapper()
    {
        DuckBenchEntity? result = null;
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = $Id";
        for (var i = 0; i < RecordCount; i++)
        {
            await using var conn = new DuckDBConnection($"Data Source={_dbFile}");
            await conn.OpenAsync();
            result = await conn.QueryFirstOrDefaultAsync<DuckBenchEntity>(
                sql, new { Id = (i % SeedRows) + 1 });
        }

        return result;
    }

    // ========================================================================
    // READ LIST BENCHMARKS
    // ========================================================================

    [Benchmark]
    public async Task<List<DuckBenchEntity>> ReadList_Pengdows()
    {
        await using var container = _pengdowsCtx.CreateSqlContainer();
        container.Query.Append(
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > ");
        container.Query.Append(container.MakeParameterName("Age"));
        container.Query.Append($" LIMIT {RecordCount}");
        container.AddParameterWithValue("Age", DbType.Int32, 30);
        return await _gateway.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DuckBenchEntity>> ReadList_Dapper()
    {
        await using var conn = new DuckDBConnection($"Data Source={_dbFile}");
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<DuckBenchEntity>(
            $"SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > $Age LIMIT {RecordCount}",
            new { Age = 30 });
        return rows.AsList();
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
            await using var container = _pengdowsCtx.CreateSqlContainer();
            container.Query.Append("UPDATE benchmark SET salary = ");
            container.Query.Append(container.MakeParameterName("Salary"));
            container.Query.Append(" WHERE id = ");
            container.Query.Append(container.MakeParameterName("Id"));
            container.AddParameterWithValue("Salary", DbType.Double, 60000.0 + i);
            container.AddParameterWithValue("Id", DbType.Int32, (i % SeedRows) + 1);
            count += await container.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<int> Update_Dapper()
    {
        const string sql = "UPDATE benchmark SET salary = $Salary WHERE id = $Id";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var conn = new DuckDBConnection($"Data Source={_dbFile}");
            await conn.OpenAsync();
            count += await conn.ExecuteAsync(sql, new
            {
                Salary = 60000.0 + i,
                Id = (i % SeedRows) + 1
            });
        }

        return count;
    }

    // ========================================================================
    // DELETE BENCHMARKS (INSERT + DELETE per iteration — equal footing)
    // ========================================================================

    [Benchmark]
    public async Task<int> Delete_Pengdows()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _deleteIdSeed);
            await using (var ins = _pengdowsCtx.CreateSqlContainer())
            {
                ins.Query.Append(
                    "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES (");
                ins.Query.Append(ins.MakeParameterName("Id"));
                ins.Query.Append(", ");
                ins.Query.Append(ins.MakeParameterName("Name"));
                ins.Query.Append(", ");
                ins.Query.Append(ins.MakeParameterName("Age"));
                ins.Query.Append(", ");
                ins.Query.Append(ins.MakeParameterName("Salary"));
                ins.Query.Append(", ");
                ins.Query.Append(ins.MakeParameterName("IsActive"));
                ins.Query.Append(", ");
                ins.Query.Append(ins.MakeParameterName("CreatedAt"));
                ins.Query.Append(')');
                ins.AddParameterWithValue("Id", DbType.Int32, id);
                ins.AddParameterWithValue("Name", DbType.String, "ToDelete");
                ins.AddParameterWithValue("Age", DbType.Int32, 99);
                ins.AddParameterWithValue("Salary", DbType.Double, 1.0);
                ins.AddParameterWithValue("IsActive", DbType.Boolean, false);
                ins.AddParameterWithValue("CreatedAt", DbType.DateTime, DateTime.UtcNow);
                await ins.ExecuteNonQueryAsync();
            }

            await using var del = _pengdowsCtx.CreateSqlContainer();
            del.Query.Append("DELETE FROM benchmark WHERE id = ");
            del.Query.Append(del.MakeParameterName("Id"));
            del.AddParameterWithValue("Id", DbType.Int32, id);
            count += await del.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<int> Delete_Dapper()
    {
        const string insertSql =
            "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES ($Id, $Name, $Age, $Salary, $IsActive, $CreatedAt)";
        const string deleteSql = "DELETE FROM benchmark WHERE id = $Id";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _deleteIdSeed);
            {
                await using var conn = new DuckDBConnection($"Data Source={_dbFile}");
                await conn.OpenAsync();
                await conn.ExecuteAsync(insertSql, new
                {
                    Id = id, Name = "ToDelete", Age = 99, Salary = 1.0,
                    IsActive = false, CreatedAt = DateTime.UtcNow
                });
            }

            {
                await using var conn = new DuckDBConnection($"Data Source={_dbFile}");
                await conn.OpenAsync();
                count += await conn.ExecuteAsync(deleteSql, new { Id = id });
            }
        }

        return count;
    }

    // ========================================================================
    // FILTERED QUERY BENCHMARKS
    // ========================================================================

    [Benchmark]
    public async Task<List<DuckBenchEntity>> FilteredQuery_Pengdows()
    {
        await using var container = _pengdowsCtx.CreateSqlContainer();
        container.Query.Append(
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE is_active = ");
        container.Query.Append(container.MakeParameterName("IsActive"));
        container.Query.Append(" AND age >= ");
        container.Query.Append(container.MakeParameterName("MinAge"));
        container.Query.Append(" AND age <= ");
        container.Query.Append(container.MakeParameterName("MaxAge"));
        container.Query.Append($" LIMIT {RecordCount}");
        container.AddParameterWithValue("IsActive", DbType.Boolean, true);
        container.AddParameterWithValue("MinAge", DbType.Int32, 25);
        container.AddParameterWithValue("MaxAge", DbType.Int32, 45);
        return await _gateway.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DuckBenchEntity>> FilteredQuery_Dapper()
    {
        await using var conn = new DuckDBConnection($"Data Source={_dbFile}");
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<DuckBenchEntity>(
            $"SELECT id, name, age, salary, is_active, created_at FROM benchmark " +
            $"WHERE is_active = TRUE AND age >= $MinAge AND age <= $MaxAge LIMIT {RecordCount}",
            new { MinAge = 25, MaxAge = 45 });
        return rows.AsList();
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
            await using var container = _pengdowsCtx.CreateSqlContainer(
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
            await using var conn = new DuckDBConnection($"Data Source={_dbFile}");
            await conn.OpenAsync();
            result = await conn.ExecuteScalarAsync<double>(
                "SELECT AVG(salary) FROM benchmark WHERE is_active = TRUE");
        }

        return result;
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
            sw.Restart();
            var container = _pengdowsCtx.CreateSqlContainer();
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
        long buildTicks = 0;
        long executeTicks = 0;

        for (var i = 0; i < RecordCount; i++)
        {
            sw.Restart();
            const string sql =
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = $Id";
            var param = new { Id = (i % SeedRows) + 1 };
            sw.Stop();
            buildTicks += sw.ElapsedTicks;

            sw.Restart();
            await using var conn = new DuckDBConnection($"Data Source={_dbFile}");
            await conn.OpenAsync();
            await conn.QueryFirstOrDefaultAsync<DuckBenchEntity>(sql, param);
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
        await using var container = _pengdowsCtx.CreateSqlContainer();
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
        await using var conn = new DuckDBConnection($"Data Source={_dbFile}");
        await conn.OpenAsync();
        await conn.QueryFirstOrDefaultAsync<DuckBenchEntity>(
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = $Id",
            new { Id = 1 });
        sw.Stop();
        return sw.ElapsedTicks;
    }

    // ========================================================================
    // ENTITY
    // All columns map to native DuckDB types — no coercion required.
    // [Id(true)]: DuckDB INTEGER PRIMARY KEY has no AUTOINCREMENT; callers
    // must supply all IDs explicitly (see _createIdSeed / _deleteIdSeed).
    // ========================================================================

    [Table("benchmark")]
    public class DuckBenchEntity
    {
        [Id(true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Column("age", DbType.Int32)] public int Age { get; set; }

        [Column("salary", DbType.Double)] public double Salary { get; set; }

        [Column("is_active", DbType.Boolean)] public bool IsActive { get; set; }

        [Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }
    }
}
