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
///   - EF Core comparison is in the net10-only DuckDbEfCoreBenchmarks project using
///     Skirrels' DuckDB.EFCoreProvider through pengdows.stormgate.EntityFrameworkCore
///   - DuckDB native types: INTEGER → int32, BOOLEAN → bool, DOUBLE → double,
///     TIMESTAMP → DateTime — zero coercion unlike SQLite's int64/TEXT returns
///   - [Id(true)] throughout: DuckDB INTEGER PRIMARY KEY has no AUTOINCREMENT;
///     all benchmark operations supply explicit IDs via atomic counters
///   - LIMIT values are inlined (DuckDB does not support named parameters in LIMIT)
///
/// All operations open and close connections per call on both frameworks,
/// matching the equal-footing methodology of EqualFootingCrudBenchmarks.
///
/// The pengdows context here runs in Standard mode (DuckDB honors it explicitly), so it has the pool governor
/// but no single-writer serialization. The plain *_Dapper arms are ungoverned (fresh connection per operation).
/// The *_DapperPinnedWriter arms are the hand-rolled single writer: one pinned writer connection behind a gate,
/// reads on their own connections. The *_DapperSingleConnection arms put reads through that same connection and
/// gate too. The *_PengdowsSingleWriter arms run pengdows in DbMode.SingleWriter, which is what Best selects for
/// a DuckDB file. Retry-on-exception is not a writer design and is not benchmarked.
///
/// pengdows.crud side uses the full TableGateway API:
///   - BuildCreate / BuildUpdateAsync / BuildDelete for write operations
///   - BuildRetrieve for keyed reads (reused container + SetParameterValue)
///   - BuildBaseRetrieve + WrapObjectName for custom queries (reused + SetParameterValue)
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class DuckDbEqualFootingBenchmarks : IDisposable
{
    private const int SeedRows = 1000;

    private string _dbFile = null!;

    // The hand-rolled equivalent of SingleWriter for the *_DapperPinnedWriter arms: one long-lived writer
    // connection, with a gate so only one write runs at a time. Dapper has nothing like this built in.
    private DuckDBConnection _dapperWriter = null!;
    private readonly SemaphoreSlim _dapperWriteGate = new(1, 1);

    // pengdows.crud — Standard mode (connection-per-op)
    private DatabaseContext _pengdowsCtx = null!;
    private TableGateway<DuckBenchEntity, int> _gateway = null!;
    private TypeMapRegistry _typeMap = null!;

    // Explicit ID ranges — seed occupies 1..1000; ranges below avoid collision
    private int _createIdSeed = 10_000;
    private int _deleteIdSeed = 100_000;

    // Reusable containers — built once in GlobalSetup using TableGateway Build* methods.
    // Sequential benchmark loops reuse the same container via SetParameterValue; no SQL
    // re-generation and no allocation per iteration.
    private ISqlContainer _readSingleSc = null!;
    private ISqlContainer _readListSc = null!;
    private ISqlContainer _filteredQuerySc = null!;
    private ISqlContainer _updateSc = null!;
    private ISqlContainer _deleteSc = null!;
    private ISqlContainer _aggregateSc = null!;

    // The fixed (non-varying) column values baked into _updateSc's DuckBenchEntity below —
    // reused by Update_Dapper so its UPDATE statement sets the same 5 columns pengdows' full-
    // entity BuildUpdateAsync generates, not just salary.
    private const string UpdateFixedName = "Updated";
    private const int UpdateFixedAge = 25;
    private const bool UpdateFixedIsActive = true;
    private DateTime _updateFixedCreatedAt;

    private bool _originalMatchNamesWithUnderscores;

    // pengdows.crud — SingleConnection mode (shared persistent connection).
    // Eliminates open/close overhead per operation — isolates the coercion cost
    // by putting pengdows on equal footing with a dedicated DuckDB connection.
    private DatabaseContext _pengdowsSingleCtx = null!;
    private TableGateway<DuckBenchEntity, int> _singleGateway = null!;
    private ISqlContainer _readSingleScSingle = null!;
    private ISqlContainer _readListScSingle = null!;

    // pengdows.crud — SingleWriter mode (governor + write turnstile, ephemeral connections). This is what
    // DbMode.Best selects for a DuckDB file, so it is the mode a default user runs; the Standard arms above
    // are kept for continuity with earlier runs.
    private DatabaseContext _pengdowsWriterCtx = null!;
    private TableGateway<DuckBenchEntity, int> _writerGateway = null!;
    private ISqlContainer _readSingleScWriter = null!;
    private ISqlContainer _readListScWriter = null!;
    private ISqlContainer _filteredQueryScWriter = null!;
    private ISqlContainer _updateScWriter = null!;
    private ISqlContainer _deleteScWriter = null!;
    private ISqlContainer _aggregateScWriter = null!;

    [Params(1, 100)] public int RecordCount { get; set; }

    // ========================================================================
    // SETUP / TEARDOWN
    // ========================================================================

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _originalMatchNamesWithUnderscores = DefaultTypeMap.MatchNamesWithUnderscores;
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _dbFile = Path.Combine(
            Path.GetTempPath(),
            $"pengdows_duckdb_eqft_{Guid.NewGuid():N}.duckdb");

        await using (var seedConn = new DuckDBConnection($"Data Source={_dbFile}"))
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

        _dapperWriter = new DuckDBConnection($"Data Source={_dbFile}");
        _dapperWriter.Open();

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

        // SingleConnection: shared persistent connection — eliminates open/close + session overhead.
        var cfgSingle = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={_dbFile}",
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.SingleConnection
        };
        _pengdowsSingleCtx = new DatabaseContext(cfgSingle, DuckDBClientFactory.Instance, null, _typeMap);
        _singleGateway = new TableGateway<DuckBenchEntity, int>(_pengdowsSingleCtx);

        // Build reusable containers once using TableGateway's SQL generation.
        // SQL is dialect-aware and cached; SetParameterValue updates values in-place
        // each iteration with zero SQL re-generation.
        // RecordCount is set by BDN before GlobalSetup runs (one setup per Params combination).

        _readSingleSc = _gateway.BuildRetrieve(new[] { 1 });
        _readSingleScSingle = _singleGateway.BuildRetrieve(new[] { 1 });

        _readListSc = _gateway.BuildBaseRetrieve("b");
        _readListSc.Query.Append($" WHERE {_pengdowsCtx.WrapObjectName("b.age")} > ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Age"));
        _readListSc.AddParameterWithValue("Age", DbType.Int32, 0);
        _readListSc.Query.Append($" LIMIT {RecordCount}");

        _readListScSingle = _singleGateway.BuildBaseRetrieve("b");
        _readListScSingle.Query.Append($" WHERE {_pengdowsSingleCtx.WrapObjectName("b.age")} > ");
        _readListScSingle.Query.Append(_readListScSingle.MakeParameterName("Age"));
        _readListScSingle.AddParameterWithValue("Age", DbType.Int32, 0);
        _readListScSingle.Query.Append($" LIMIT {RecordCount}");

        _filteredQuerySc = _gateway.BuildBaseRetrieve("b");
        _filteredQuerySc.Query.Append($" WHERE {_pengdowsCtx.WrapObjectName("b.is_active")} = ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("IsActive"));
        _filteredQuerySc.AddParameterWithValue("IsActive", DbType.Boolean, true);
        _filteredQuerySc.Query.Append($" AND {_pengdowsCtx.WrapObjectName("b.age")} >= ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("MinAge"));
        _filteredQuerySc.AddParameterWithValue("MinAge", DbType.Int32, 25);
        _filteredQuerySc.Query.Append($" AND {_pengdowsCtx.WrapObjectName("b.age")} <= ");
        _filteredQuerySc.Query.Append(_filteredQuerySc.MakeParameterName("MaxAge"));
        _filteredQuerySc.AddParameterWithValue("MaxAge", DbType.Int32, 45);
        _filteredQuerySc.Query.Append($" LIMIT {RecordCount}");

        // Update: pre-build the full UPDATE statement once; vary salary + id per iteration
        // via SetParameterValue. Column param order: s0=name, s1=age, s2=salary,
        // s3=is_active, s4=created_at, k0=id (WHERE).
        _updateFixedCreatedAt = DateTime.UtcNow;
        _updateSc = await _gateway.BuildUpdateAsync(new DuckBenchEntity
        {
            Id = 1,
            Name = UpdateFixedName,
            Age = UpdateFixedAge,
            Salary = 50000.0,
            IsActive = UpdateFixedIsActive,
            CreatedAt = _updateFixedCreatedAt
        });

        _deleteSc = _gateway.BuildDelete(0);

        _aggregateSc = _pengdowsCtx.CreateSqlContainer(
            "SELECT AVG(salary) FROM benchmark WHERE is_active = TRUE");

        // SingleWriter context and the same containers built against it.
        var cfgWriter = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={_dbFile}",
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.SingleWriter
        };
        _pengdowsWriterCtx = new DatabaseContext(cfgWriter, DuckDBClientFactory.Instance, null, _typeMap);
        _writerGateway = new TableGateway<DuckBenchEntity, int>(_pengdowsWriterCtx);

        _readSingleScWriter = _writerGateway.BuildRetrieve(new[] { 1 });

        _readListScWriter = _writerGateway.BuildBaseRetrieve("b");
        _readListScWriter.Query.Append($" WHERE {_pengdowsWriterCtx.WrapObjectName("b.age")} > ");
        _readListScWriter.Query.Append(_readListScWriter.MakeParameterName("Age"));
        _readListScWriter.AddParameterWithValue("Age", DbType.Int32, 0);
        _readListScWriter.Query.Append($" LIMIT {RecordCount}");

        _filteredQueryScWriter = _writerGateway.BuildBaseRetrieve("b");
        _filteredQueryScWriter.Query.Append($" WHERE {_pengdowsWriterCtx.WrapObjectName("b.is_active")} = ");
        _filteredQueryScWriter.Query.Append(_filteredQueryScWriter.MakeParameterName("IsActive"));
        _filteredQueryScWriter.AddParameterWithValue("IsActive", DbType.Boolean, true);
        _filteredQueryScWriter.Query.Append($" AND {_pengdowsWriterCtx.WrapObjectName("b.age")} >= ");
        _filteredQueryScWriter.Query.Append(_filteredQueryScWriter.MakeParameterName("MinAge"));
        _filteredQueryScWriter.AddParameterWithValue("MinAge", DbType.Int32, 25);
        _filteredQueryScWriter.Query.Append($" AND {_pengdowsWriterCtx.WrapObjectName("b.age")} <= ");
        _filteredQueryScWriter.Query.Append(_filteredQueryScWriter.MakeParameterName("MaxAge"));
        _filteredQueryScWriter.AddParameterWithValue("MaxAge", DbType.Int32, 45);
        _filteredQueryScWriter.Query.Append($" LIMIT {RecordCount}");

        _updateScWriter = await _writerGateway.BuildUpdateAsync(new DuckBenchEntity
        {
            Id = 1,
            Name = UpdateFixedName,
            Age = UpdateFixedAge,
            Salary = 50000.0,
            IsActive = UpdateFixedIsActive,
            CreatedAt = _updateFixedCreatedAt
        });

        _deleteScWriter = _writerGateway.BuildDelete(0);

        _aggregateScWriter = _pengdowsWriterCtx.CreateSqlContainer(
            "SELECT AVG(salary) FROM benchmark WHERE is_active = TRUE");
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

        _readSingleSc?.Dispose();
        _readListSc?.Dispose();
        _readSingleScSingle?.Dispose();
        _readListScSingle?.Dispose();
        _filteredQuerySc?.Dispose();
        _updateSc?.Dispose();
        _deleteSc?.Dispose();
        _aggregateSc?.Dispose();

        _readSingleScWriter?.Dispose();
        _readListScWriter?.Dispose();
        _filteredQueryScWriter?.Dispose();
        _updateScWriter?.Dispose();
        _deleteScWriter?.Dispose();
        _aggregateScWriter?.Dispose();

        _pengdowsCtx?.Dispose();
        _pengdowsSingleCtx?.Dispose();
        _pengdowsWriterCtx?.Dispose();
        _dapperWriter?.Dispose();

        foreach (var f in new[] { _dbFile, _dbFile + ".wal" })
        {
            if (File.Exists(f))
            {
                try
                {
                    File.Delete(f);
                }
                catch
                {
                    /* best-effort */
                }
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
            var entity = new DuckBenchEntity
            {
                Id = id,
                Name = $"Created {id}",
                Age = 25,
                Salary = 50000.0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
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
            _readSingleSc.SetParameterValue("w0", (i % SeedRows) + 1);
            result = await _gateway.LoadSingleAsync(_readSingleSc);
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

    /// <summary>
    /// pengdows.crud SingleConnection — shared persistent connection, no open/close per op.
    /// Isolates the type-coercion cost by eliminating connection overhead from the measurement.
    /// Compare ratio (SingleConnection/Dapper) vs (Standard/Dapper) to quantify coercion impact.
    /// </summary>
    [Benchmark]
    public async Task<DuckBenchEntity?> ReadSingle_Pengdows_SingleConnection()
    {
        DuckBenchEntity? result = null;
        for (var i = 0; i < RecordCount; i++)
        {
            _readSingleScSingle.SetParameterValue("w0", (i % SeedRows) + 1);
            result = await _singleGateway.LoadSingleAsync(_readSingleScSingle);
        }

        return result;
    }

    // ========================================================================
    // READ LIST BENCHMARKS
    // ========================================================================

    [Benchmark]
    public async Task<List<DuckBenchEntity>> ReadList_Pengdows()
    {
        _readListSc.SetParameterValue("Age", 30);
        return await _gateway.LoadListAsync(_readListSc);
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

    /// <summary>
    /// pengdows.crud SingleConnection list read — shared persistent connection.
    /// </summary>
    [Benchmark]
    public async Task<List<DuckBenchEntity>> ReadList_Pengdows_SingleConnection()
    {
        _readListScSingle.SetParameterValue("Age", 30);
        return await _singleGateway.LoadListAsync(_readListScSingle);
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
            _updateSc.SetParameterValue("s2", 60000.0 + i);
            _updateSc.SetParameterValue("k0", (i % SeedRows) + 1);
            count += await _updateSc.ExecuteNonQueryAsync();
        }

        return count;
    }

    // Sets the same 5 columns as Update_Pengdows (name, age, salary, is_active, created_at) —
    // only salary and id vary per iteration, matching _updateSc's fixed values. See
    // UpdateFixedName/UpdateFixedAge/UpdateFixedIsActive/_updateFixedCreatedAt.
    private const string UpdateSql =
        "UPDATE benchmark SET name = $Name, age = $Age, salary = $Salary, is_active = $IsActive, created_at = $CreatedAt WHERE id = $Id";

    [Benchmark]
    public async Task<int> Update_Dapper()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var conn = new DuckDBConnection($"Data Source={_dbFile}");
            await conn.OpenAsync();
            count += await conn.ExecuteAsync(UpdateSql, new
            {
                Name = UpdateFixedName,
                Age = UpdateFixedAge,
                Salary = 60000.0 + i,
                IsActive = UpdateFixedIsActive,
                CreatedAt = _updateFixedCreatedAt,
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
            var entity = new DuckBenchEntity
            {
                Id = id,
                Name = "ToDelete",
                Age = 99,
                Salary = 1.0,
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            };
            await using var ins = _gateway.BuildCreate(entity);
            await ins.ExecuteNonQueryAsync();

            _deleteSc.SetParameterValue("k0", id);
            count += await _deleteSc.ExecuteNonQueryAsync();
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
                    Id = id,
                    Name = "ToDelete",
                    Age = 99,
                    Salary = 1.0,
                    IsActive = false,
                    CreatedAt = DateTime.UtcNow
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
    // PINNED-WRITER DAPPER ARMS
    // Every write goes through one long-lived writer connection, one at a time (_dapperWriteGate).
    // Compare with the *_Pengdows arms; the plain *_Dapper arms are the ungoverned baseline.
    // ========================================================================

    [Benchmark]
    public async Task<int> Create_DapperPinnedWriter()
    {
        const string sql =
            "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES ($Id, $Name, $Age, $Salary, $IsActive, $CreatedAt)";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _createIdSeed);
            await _dapperWriteGate.WaitAsync();
            try
            {
                count += await _dapperWriter.ExecuteAsync(sql, new
                {
                    Id = id,
                    Name = $"Created {id}",
                    Age = 25,
                    Salary = 50000.0,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }
            finally
            {
                _dapperWriteGate.Release();
            }
        }

        return count;
    }

    [Benchmark]
    public async Task<int> Update_DapperPinnedWriter()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await _dapperWriteGate.WaitAsync();
            try
            {
                count += await _dapperWriter.ExecuteAsync(UpdateSql, new
                {
                    Name = UpdateFixedName,
                    Age = UpdateFixedAge,
                    Salary = 60000.0 + i,
                    IsActive = UpdateFixedIsActive,
                    CreatedAt = _updateFixedCreatedAt,
                    Id = (i % SeedRows) + 1
                });
            }
            finally
            {
                _dapperWriteGate.Release();
            }
        }

        return count;
    }

    [Benchmark]
    public async Task<int> Delete_DapperPinnedWriter()
    {
        const string insertSql =
            "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES ($Id, $Name, $Age, $Salary, $IsActive, $CreatedAt)";
        const string deleteSql = "DELETE FROM benchmark WHERE id = $Id";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _deleteIdSeed);

            await _dapperWriteGate.WaitAsync();
            try
            {
                await _dapperWriter.ExecuteAsync(insertSql, new
                {
                    Id = id,
                    Name = "ToDelete",
                    Age = 99,
                    Salary = 1.0,
                    IsActive = false,
                    CreatedAt = DateTime.UtcNow
                });
            }
            finally
            {
                _dapperWriteGate.Release();
            }

            await _dapperWriteGate.WaitAsync();
            try
            {
                count += await _dapperWriter.ExecuteAsync(deleteSql, new { Id = id });
            }
            finally
            {
                _dapperWriteGate.Release();
            }
        }

        return count;
    }

    // ========================================================================
    // SINGLE-CONNECTION DAPPER ARMS (reads)
    // One connection and one lock, and everything (reads too) goes through them. Its write arms are the
    // *_DapperPinnedWriter ones above (there is only the one connection).
    // ========================================================================

    [Benchmark]
    public async Task<DuckBenchEntity?> ReadSingle_DapperSingleConnection()
    {
        DuckBenchEntity? result = null;
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = $Id";
        for (var i = 0; i < RecordCount; i++)
        {
            await _dapperWriteGate.WaitAsync();
            try
            {
                result = await _dapperWriter.QueryFirstOrDefaultAsync<DuckBenchEntity>(
                    sql, new { Id = (i % SeedRows) + 1 });
            }
            finally
            {
                _dapperWriteGate.Release();
            }
        }

        return result;
    }

    [Benchmark]
    public async Task<List<DuckBenchEntity>> ReadList_DapperSingleConnection()
    {
        await _dapperWriteGate.WaitAsync();
        try
        {
            var rows = await _dapperWriter.QueryAsync<DuckBenchEntity>(
                $"SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > $Age LIMIT {RecordCount}",
                new { Age = 30 });
            return rows.AsList();
        }
        finally
        {
            _dapperWriteGate.Release();
        }
    }

    [Benchmark]
    public async Task<List<DuckBenchEntity>> FilteredQuery_DapperSingleConnection()
    {
        await _dapperWriteGate.WaitAsync();
        try
        {
            var rows = await _dapperWriter.QueryAsync<DuckBenchEntity>(
                $"SELECT id, name, age, salary, is_active, created_at FROM benchmark " +
                $"WHERE is_active = TRUE AND age >= $MinAge AND age <= $MaxAge LIMIT {RecordCount}",
                new { MinAge = 25, MaxAge = 45 });
            return rows.AsList();
        }
        finally
        {
            _dapperWriteGate.Release();
        }
    }

    [Benchmark]
    public async Task<double> Aggregate_DapperSingleConnection()
    {
        double result = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await _dapperWriteGate.WaitAsync();
            try
            {
                result = await _dapperWriter.ExecuteScalarAsync<double>(
                    "SELECT AVG(salary) FROM benchmark WHERE is_active = TRUE");
            }
            finally
            {
                _dapperWriteGate.Release();
            }
        }

        return result;
    }

    // ========================================================================
    // PENGDOWS SINGLEWRITER ARMS
    // The same operations as the *_Pengdows arms, on a DbMode.SingleWriter context (what Best selects for a
    // DuckDB file). Compare these with the Dapper pinned-writer / single-connection arms.
    // ========================================================================

    [Benchmark]
    public async Task<int> Create_PengdowsSingleWriter()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _createIdSeed);
            var entity = new DuckBenchEntity
            {
                Id = id,
                Name = $"Created {id}",
                Age = 25,
                Salary = 50000.0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await using var sc = _writerGateway.BuildCreate(entity);
            count += await sc.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<DuckBenchEntity?> ReadSingle_PengdowsSingleWriter()
    {
        DuckBenchEntity? result = null;
        for (var i = 0; i < RecordCount; i++)
        {
            _readSingleScWriter.SetParameterValue("w0", (i % SeedRows) + 1);
            result = await _writerGateway.LoadSingleAsync(_readSingleScWriter);
        }

        return result;
    }

    [Benchmark]
    public async Task<List<DuckBenchEntity>> ReadList_PengdowsSingleWriter()
    {
        _readListScWriter.SetParameterValue("Age", 30);
        return await _writerGateway.LoadListAsync(_readListScWriter);
    }

    [Benchmark]
    public async Task<int> Update_PengdowsSingleWriter()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            _updateScWriter.SetParameterValue("s2", 60000.0 + i);
            _updateScWriter.SetParameterValue("k0", (i % SeedRows) + 1);
            count += await _updateScWriter.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<int> Delete_PengdowsSingleWriter()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _deleteIdSeed);
            var entity = new DuckBenchEntity
            {
                Id = id,
                Name = "ToDelete",
                Age = 99,
                Salary = 1.0,
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            };
            await using var ins = _writerGateway.BuildCreate(entity);
            await ins.ExecuteNonQueryAsync();

            _deleteScWriter.SetParameterValue("k0", id);
            count += await _deleteScWriter.ExecuteNonQueryAsync();
        }

        return count;
    }

    [Benchmark]
    public async Task<List<DuckBenchEntity>> FilteredQuery_PengdowsSingleWriter()
    {
        _filteredQueryScWriter.SetParameterValue("IsActive", true);
        _filteredQueryScWriter.SetParameterValue("MinAge", 25);
        _filteredQueryScWriter.SetParameterValue("MaxAge", 45);
        return await _writerGateway.LoadListAsync(_filteredQueryScWriter);
    }

    [Benchmark]
    public async Task<double> Aggregate_PengdowsSingleWriter()
    {
        double result = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            result = await _aggregateScWriter.ExecuteScalarOrNullAsync<double>();
        }

        return result;
    }

    // ========================================================================
    // FILTERED QUERY BENCHMARKS
    // ========================================================================

    [Benchmark]
    public async Task<List<DuckBenchEntity>> FilteredQuery_Pengdows()
    {
        _filteredQuerySc.SetParameterValue("IsActive", true);
        _filteredQuerySc.SetParameterValue("MinAge", 25);
        _filteredQuerySc.SetParameterValue("MaxAge", 45);
        return await _gateway.LoadListAsync(_filteredQuerySc);
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
            await using var conn = new DuckDBConnection($"Data Source={_dbFile}");
            await conn.OpenAsync();
            result = await conn.ExecuteScalarAsync<double>(
                "SELECT AVG(salary) FROM benchmark WHERE is_active = TRUE");
        }

        return result;
    }

    // ========================================================================
    // BREAKDOWN: BUILD vs EXECUTE timing
    // Measures BuildRetrieve (SQL generation + parameter setup) vs LoadSingleAsync
    // (connection open + execute + map + close) separately.
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
        _readSingleSc.SetParameterValue("w0", 1);
        await _gateway.LoadSingleAsync(_readSingleSc);
        sw.Stop();
        return sw.ElapsedTicks;
    }

    [Benchmark]
    public async Task<long> ConnectionHoldTime_Dapper()
    {
        // Boundary matches Pengdows: the watch stops only after the connection has been
        // released, not before disposal runs. An `await using` declaration defers
        // DisposeAsync() to end-of-method, which would stop the watch before paying that cost.
        var sw = Stopwatch.StartNew();
        var conn = new DuckDBConnection($"Data Source={_dbFile}");
        try
        {
            await conn.OpenAsync();
            await conn.QueryFirstOrDefaultAsync<DuckBenchEntity>(
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = $Id",
                new { Id = 1 });
        }
        finally
        {
            await conn.DisposeAsync();
        }
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
