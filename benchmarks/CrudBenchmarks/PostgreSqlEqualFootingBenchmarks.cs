using System.Data;
using System.Data.Common;
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
/// pengdows.crud side uses the full TableGateway API:
///   - BuildCreate for INSERT (framework generates dialect-correct INSERT, type-safe params)
///   - BuildRetrieve for keyed reads (reused container + SetParameterValue)
///   - BuildBaseRetrieve + WrapObjectName for custom queries (reused + SetParameterValue)
///   - BuildUpdateAsync for full UPDATE (reused container + SetParameterValue on changed fields)
///   - BuildDelete for DELETE (reused container + SetParameterValue)
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


    private DbDataSource? _dataSource = null;

    // pengdows.crud
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<BenchEntity, int> _gateway = null!;
    private TypeMapRegistry _typeMap = null!;

    // Entity Framework
    private DbContextOptions<EfPgBenchContext> _efOptions = null!;

    // Unique ID seed for delete benchmarks — start high to avoid colliding with SERIAL IDs
    private int _deleteIdSeed = 1_000_000;

    // Reusable ISqlContainer fields — built once in GlobalSetup using TableGateway Build* methods.
    // Sequential benchmark loops reuse the same container via SetParameterValue; no SQL
    // re-generation and no allocation per iteration.
    private ISqlContainer _readSingleSc = null!;
    private ISqlContainer _readListSc = null!;
    private ISqlContainer _filteredQuerySc = null!;
    private ISqlContainer _updateSc = null!;
    private ISqlContainer _deleteInsertSc = null!;
    private ISqlContainer _deleteSc = null!;
    private ISqlContainer _aggregateSc = null!;

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
        await using var conn = await GetDapperEqualConnection();


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

        // ---- Build reusable containers once ----

        // ReadSingle — keyed by id collection; loop calls SetParameterValue("w0", id) (scalar)
        _readSingleSc = _gateway.BuildRetrieve(new[] { 1 });

        // ReadList — BuildBaseRetrieve + custom WHERE age > @Age LIMIT @Limit
        _readListSc = _gateway.BuildBaseRetrieve("b");
        _readListSc.Query.Append($" WHERE {_pengdowsContext.WrapObjectName("b.age")} > ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Age"));
        _readListSc.AddParameterWithValue("Age", DbType.Int32, 0);
        _readListSc.Query.Append(" LIMIT ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Limit"));
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

        // Delete insert side — explicit id required since SERIAL would otherwise auto-assign.
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
            "SELECT AVG(salary) FROM benchmark WHERE is_active = TRUE");

        // ---- Npgsql auto-prepare pre-warming ----
        //
        // DatabaseContext bakes MaxAutoPrepare=64 / AutoPrepareMinUsages=2 into the Npgsql
        // DataSource, so Npgsql server-side prepares statements after 2 uses *per connection*.
        // With Minimum Pool Size=5, all 5 connections are pre-created.  BenchmarkDotNet
        // warmupCount=3 at RecordCount=1 gives only 3 sequential executions — too few to
        // saturate all pool connections, so some stay unprepared when measurement begins.
        //
        // Fix: run each container (minPoolSize × threshold × safety) = 5 × 2 × 2 = 20 times
        // before BDN warmup starts.  Sequential LIFO means traffic concentrates on 1-2
        // connections, so 20 iterations reliably drives all 5 pool connections past the
        // AutoPrepareMinUsages threshold.
        const int prewarmCount = 20;
        const int prewarmDeleteIdBase = 5_000_000; // separate range, won't collide with benchmark

        await PreWarmFrameworkCachesAsync(prewarmCount, prewarmDeleteIdBase);
    }

    private async Task PreWarmFrameworkCachesAsync(int prewarmCount, int prewarmDeleteIdBase)
    {
        const string createSql =
            "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES (@Name, @Age, @Salary, @IsActive, @CreatedAt)";
        const string readSingleSql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id";
        const string readListSql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > @Age LIMIT @Limit";
        const string updateSql = "UPDATE benchmark SET salary = @Salary WHERE id = @Id";
        const string deleteInsertSql =
            "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES (@Id, @Name, @Age, @Salary, @IsActive, @CreatedAt)";
        const string deleteSql = "DELETE FROM benchmark WHERE id = @Id";
        const string filteredQuerySql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE is_active = @IsActive AND age >= @MinAge AND age <= @MaxAge LIMIT @Limit";
        const string aggregateSql = "SELECT AVG(salary) FROM benchmark WHERE is_active = TRUE";

        for (var pw = 0; pw < prewarmCount; pw++)
        {
            var createdAt = DateTime.UtcNow.ToString("O");

            // ReadSingle
            _readSingleSc.SetParameterValue("w0", (pw % SeedRows) + 1);
            await _gateway.LoadSingleAsync(_readSingleSc);

            // ReadList (Age=0 returns all rows limited by RecordCount — valid warmup)
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

            // Delete: insert then delete (separate ID range — no collision with benchmark iterations)
            var prewarmId = prewarmDeleteIdBase + pw;
            _deleteInsertSc.SetParameterValue("Id", prewarmId);
            await _deleteInsertSc.ExecuteNonQueryAsync();
            _deleteSc.SetParameterValue("k0", prewarmId);
            await _deleteSc.ExecuteNonQueryAsync();

            await using (var tx = await _pengdowsContext.BeginTransactionAsync())
            await using (var deleteOnlySc = _deleteSc.Clone(tx))
            {
                deleteOnlySc.SetParameterValue("k0", (pw % SeedRows) + 1);
                await deleteOnlySc.ExecuteNonQueryAsync();
                tx.Rollback();
            }

            // Aggregate
            await _aggregateSc.ExecuteScalarOrNullAsync<double>();

            // Create — new container per call (same SQL text, so Npgsql still accumulates
            // per-connection usage counts and prepares it after threshold is reached)
            var warmupEntity = new BenchEntity
            {
                Name = $"Warmup {pw}", Age = 25, Salary = 50000.0,
                IsActive = true, CreatedAt = createdAt
            };
            await using var warmupCreateSc = _gateway.BuildCreate(warmupEntity);
            await warmupCreateSc.ExecuteNonQueryAsync();

            await using (var conn = await GetDapperEqualConnection())
            {
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
                await conn.ExecuteAsync(deleteInsertSql, new
                {
                    Id = prewarmDeleteIdBase + pw,
                    Name = "ToDelete",
                    Age = 99,
                    Salary = 1.0,
                    IsActive = false,
                    CreatedAt = createdAt
                });
                await conn.ExecuteAsync(deleteSql, new { Id = prewarmDeleteIdBase + pw });
                await conn.ExecuteScalarAsync<double>(aggregateSql);
            }

            await using (var deleteOnlyConn = await GetDapperEqualConnection())
            {
                await using var tx = await deleteOnlyConn.BeginTransactionAsync();
                await deleteOnlyConn.ExecuteAsync(deleteSql, new { Id = (pw % SeedRows) + 1 }, tx);
                await tx.RollbackAsync();
            }

            await using (var efCtx = new EfPgBenchContext(_efOptions))
            {
                await efCtx.Database.ExecuteSqlRawAsync(createSql,
                    new NpgsqlParameter("Name", $"Warmup {pw}"),
                    new NpgsqlParameter("Age", 25),
                    new NpgsqlParameter("Salary", 50000.0),
                    new NpgsqlParameter("IsActive", true),
                    new NpgsqlParameter("CreatedAt", createdAt));
                _ = await efCtx.Benchmarks
                    .FromSqlRaw(readSingleSql, new NpgsqlParameter("Id", (pw % SeedRows) + 1))
                    .AsNoTracking()
                    .FirstOrDefaultAsync();
                _ = await efCtx.Benchmarks
                    .FromSqlRaw(readListSql,
                        new NpgsqlParameter { ParameterName = "Age", Value = 0 },
                        new NpgsqlParameter("Limit", RecordCount))
                    .AsNoTracking()
                    .ToListAsync();
                _ = await efCtx.Benchmarks
                    .FromSqlRaw(filteredQuerySql,
                        new NpgsqlParameter("IsActive", true),
                        new NpgsqlParameter("MinAge", 20),
                        new NpgsqlParameter("MaxAge", 60),
                        new NpgsqlParameter("Limit", RecordCount))
                    .AsNoTracking()
                    .ToListAsync();
                await efCtx.Database.ExecuteSqlRawAsync(updateSql,
                    new NpgsqlParameter("Salary", 60000.0 + pw),
                    new NpgsqlParameter("Id", (pw % SeedRows) + 1));
                await efCtx.Database.ExecuteSqlRawAsync(deleteInsertSql,
                    new NpgsqlParameter("Id", prewarmDeleteIdBase + pw),
                    new NpgsqlParameter("Name", "ToDelete"),
                    new NpgsqlParameter("Age", 99),
                    new NpgsqlParameter("Salary", 1.0),
                    new NpgsqlParameter("IsActive", false),
                    new NpgsqlParameter("CreatedAt", createdAt));
                await efCtx.Database.ExecuteSqlRawAsync(deleteSql,
                    new NpgsqlParameter("Id", prewarmDeleteIdBase + pw));
                await efCtx.Database.SqlQueryRaw<double>(aggregateSql).FirstAsync();
            }

            await using (var efDeleteCtx = new EfPgBenchContext(_efOptions))
            {
                await using var tx = await efDeleteCtx.Database.BeginTransactionAsync();
                await efDeleteCtx.Database.ExecuteSqlRawAsync(deleteSql,
                    new NpgsqlParameter("Id", (pw % SeedRows) + 1));
                await tx.RollbackAsync();
            }
        }
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _readSingleSc?.Dispose();
        _readListSc?.Dispose();
        _filteredQuerySc?.Dispose();
        _updateSc?.Dispose();
        _deleteInsertSc?.Dispose();
        _deleteSc?.Dispose();
        _aggregateSc?.Dispose();
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
            await using var conn = await GetDapperEqualConnection();
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
            await using var conn = await GetDapperEqualConnection();
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
        _readListSc.SetParameterValue("Age", 30);
        return await _gateway.LoadListAsync(_readListSc);
    }

    [Benchmark]
    public async Task<List<DapperBenchEntity>> ReadList_Dapper()
    {
        const string sql =
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > @Age LIMIT @Limit";
        await using var conn = await GetDapperEqualConnection();
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
            await using var conn = await GetDapperEqualConnection();
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
            await using var conn = await GetDapperEqualConnection();
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
            await using var ctx = new EfPgBenchContext(_efOptions);
            await using var tx = await ctx.Database.BeginTransactionAsync();
            count += await ctx.Database.ExecuteSqlRawAsync(deleteSql,
                new NpgsqlParameter("Id", (i % SeedRows) + 1));
            await tx.RollbackAsync();
        }

        return count;
    }

    /// <summary>
    /// Measures the full write lifecycle for "create a disposable row, then delete it".
    /// This is intentionally not a pure delete benchmark.
    /// </summary>
    [Benchmark]
    public async Task<int> DeleteInsertCycle_Pengdows()
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
    public async Task<int> DeleteInsertCycle_Dapper()
    {
        const string insertSql =
            "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES (@Id, @Name, @Age, @Salary, @IsActive, @CreatedAt)";
        const string deleteSql = "DELETE FROM benchmark WHERE id = @Id";
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = Interlocked.Increment(ref _deleteIdSeed);
            {
                await using var conn = await GetDapperEqualConnection();
                await conn.ExecuteAsync(insertSql, new
                {
                    Id = id, Name = "ToDelete", Age = 99, Salary = 1.0,
                    IsActive = false, CreatedAt = DateTime.UtcNow.ToString("O")
                });
            }
            {
                await using var conn = await GetDapperEqualConnection();
                count += await conn.ExecuteAsync(deleteSql, new { Id = id });
            }
        }

        return count;
    }

    [Benchmark]
    public async Task<int> DeleteInsertCycle_EntityFramework()
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
                count += await ctx.Database.ExecuteSqlRawAsync(deleteSql, new NpgsqlParameter("Id", id));
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
        await using var conn = await GetDapperEqualConnection();
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
            await using var conn = await GetDapperEqualConnection();
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
            // Measure the full Build* cost: SQL generation + parameter creation
            sw.Restart();
            var sc = _gateway.BuildRetrieve(new[] { (i % SeedRows) + 1 });
            sw.Stop();
            buildTicks += sw.ElapsedTicks;

            // Measure execute phase: connection acquire + wire + mapping
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
            await using var conn = await GetDapperEqualConnection();
            await conn.QueryFirstOrDefaultAsync<DapperBenchEntity>(sql, param);
            await conn.CloseAsync();
            sw.Stop();
            executeTicks += sw.ElapsedTicks;
        }

        return (buildTicks, executeTicks);
    }

    /// <summary>
    /// Opens an Npgsql connection and applies the same session settings that
    /// pengdows.crud injects on every connection checkout. Without this, Dapper
    /// benchmarks skip a round-trip that pengdows.crud always pays, making the
    /// comparison unfair.
    /// </summary>
    private async Task<NpgsqlConnection> GetDapperEqualConnection()
    {
        _dataSource ??= NpgsqlFactory.Instance.CreateDataSource(_connStr);

        NpgsqlConnection? conn = null;
        try
        {
            conn = _dataSource.CreateConnection() as NpgsqlConnection;
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            // cmd.CommandText =
            //     "SET standard_conforming_strings = on;\nSET client_min_messages = warning;\nSET default_transaction_read_only = off;";
            // await cmd.ExecuteNonQueryAsync();
            return conn;
        }
        catch
        {
            if (conn != null) await conn.DisposeAsync();
            throw;
        }
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
        _readSingleSc.SetParameterValue("w0", 1);
        await _gateway.LoadSingleAsync(_readSingleSc);
        sw.Stop();
        return sw.ElapsedTicks;
    }

    [Benchmark]
    public async Task<long> ConnectionHoldTime_Dapper()
    {
        var sw = Stopwatch.StartNew();
        await using var conn = await GetDapperEqualConnection();
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
        public EfPgBenchContext(DbContextOptions<EfPgBenchContext> options) : base(options)
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
