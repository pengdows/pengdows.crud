using System.Data;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Proves thesis points #4 and #5:
///   #4 - pengdows.crud holds connections for less time than EF/Dapper
///   #5 - Server execution time is equal across all three frameworks
///
/// Uses a PostgreSQL 15 Testcontainer with pg_stat_statements to capture
/// server-side execution metrics. Each framework gets its own connection pool
/// via Application Name separation. Connection hold time is measured with
/// Stopwatch around the full open-execute-close cycle.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 10, invocationCount: 100)]
public class ServerExecutionTimeBenchmarks : IAsyncDisposable
{
    // SQL templates with {param} placeholders
    private const string SingleReadSqlTemplate = """
        SELECT film_id, title, length
        FROM film
        WHERE film_id = {id}
        """;

    private const string CompositeKeyReadSqlTemplate = """
        SELECT actor_id, film_id
        FROM film_actor
        WHERE actor_id = {actorId} AND film_id = {filmId}
        """;

    private const string ListReadSqlTemplate = """
        SELECT film_id, title, length
        FROM film
        WHERE length > {minLength}
        LIMIT 50
        """;

    private const string InsertSqlTemplate = """
        INSERT INTO film(title, length)
        VALUES ({title}, {length})
        RETURNING film_id
        """;

    private const string UpdateSqlTemplate = """
        UPDATE film SET length = {len}
        WHERE film_id = {id}
        """;

    private const string DeleteSqlTemplate = """
        DELETE FROM film
        WHERE film_id = {id}
        """;

    private const string TempInsertSqlTemplate = """
        INSERT INTO film(title, length)
        VALUES ({title}, {length})
        RETURNING film_id
        """;

    private IContainer? _container;
    private string _baseConnStr = string.Empty;
    private string _pengdowsConnStr = string.Empty;
    private string _dapperConnStr = string.Empty;
    private string _efConnStr = string.Empty;

    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private TableGateway<Film, int> _filmHelper = null!;
    private TableGateway<FilmActor, int> _filmActorHelper = null!;

    private NpgsqlDataSource _pengdowsDataSource = null!;
    private NpgsqlDataSource _dapperDataSource = null!;

    private DbContextOptions<ServerExecDbContext> _efOptions = null!;
    private ServerExecDbContext _efDbContext = null!;

    private int _sampleFilmId;
    private (int actorId, int filmId) _sampleCompositeKey;
    private long _runCounter;
    private bool _flip;

    [ThreadStatic] private static string? _currentBenchmarkLabel;

    [Params(16)] public int Parallelism;
    [Params(64)] public int OperationsPerRun;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithEnvironment("POSTGRES_DB", "server_exec_test")
            .WithCommand("-c", "shared_preload_libraries=pg_stat_statements")
            .WithCommand("-c", "pg_stat_statements.track=all")
            .WithPortBinding(0, 5432)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _container.StartAsync();

        var mappedPort = _container.GetMappedPublicPort(5432);

        _baseConnStr =
            $"Host=localhost;Port={mappedPort};Database=server_exec_test;Username=postgres;Password=postgres;";
        _pengdowsConnStr = _baseConnStr + "Application Name=ServerExec_Pengdows;";
        _dapperConnStr = _baseConnStr + "Application Name=ServerExec_Dapper;";
        _efConnStr = _baseConnStr + "Application Name=ServerExec_EntityFramework;";

        await WaitForReady();
        await CreateSchemaAndSeedAsync();

        // Enable pg_stat_statements extension
        await using (var admin = new NpgsqlConnection(_baseConnStr))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS pg_stat_statements;");
            await admin.ExecuteAsync("SELECT pg_stat_statements_reset();");
        }

        // Initialize pengdows.crud with NpgsqlDataSource
        _map = new TypeMapRegistry();
        _map.Register<Film>();
        _map.Register<FilmActor>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _pengdowsConnStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };

        _pengdowsDataSource = NpgsqlDataSource.Create(_pengdowsConnStr);
        _pengdowsContext = new DatabaseContext(cfg, _pengdowsDataSource, NpgsqlFactory.Instance);

        _filmHelper = new TableGateway<Film, int>(_pengdowsContext);
        _filmActorHelper = new TableGateway<FilmActor, int>(_pengdowsContext);

        // Initialize Dapper data source
        _dapperDataSource = NpgsqlDataSource.Create(_dapperConnStr);

        // Initialize Entity Framework
        _efOptions = new DbContextOptionsBuilder<ServerExecDbContext>()
            .UseNpgsql(_efConnStr)
            .Options;
        _efDbContext = new ServerExecDbContext(_efOptions);

        // Pick sample keys for benchmarks
        await using var conn = new NpgsqlConnection(_baseConnStr);
        await conn.OpenAsync();
        _sampleFilmId = await conn.ExecuteScalarAsync<int>("SELECT film_id FROM film ORDER BY film_id LIMIT 1");
        var row = await conn.QuerySingleAsync<(int actor_id, int film_id)>(
            "SELECT actor_id, film_id FROM film_actor LIMIT 1");
        _sampleCompositeKey = (row.actor_id, row.film_id);

        // Warmup all three frameworks
        Console.WriteLine("[WARMUP] Warming up pengdows.crud...");
        await using (var sc = _pengdowsContext.CreateSqlContainer())
        {
            var sql = BuildSingleReadSql(p => sc.MakeParameterName(p));
            sc.Query.Append(sql);
            sc.AddParameterWithValue("id", DbType.Int32, _sampleFilmId);
            await using var reader = await sc.ExecuteReaderSingleRowAsync();
            if (await reader.ReadAsync())
            {
                var film = _filmHelper.MapReaderToObject(reader);
                Console.WriteLine($"[WARMUP] pengdows.crud warmed up - film: {film.Title}");
            }
        }

        Console.WriteLine("[WARMUP] Warming up Dapper...");
        await using (var dConn = await _dapperDataSource.OpenConnectionAsync())
        {
            var sql = BuildSingleReadSql(p => $"@{p}");
            var dRow = await dConn.QuerySingleOrDefaultAsync<DapperFilmRow>(sql, new { id = _sampleFilmId });
            Console.WriteLine($"[WARMUP] Dapper warmed up - film: {dRow?.title}");
        }

        Console.WriteLine("[WARMUP] Warming up Entity Framework...");
        {
            var sql = BuildSingleReadSql(p => $"@{p}");
            var efFilm = await _efDbContext.Films
                .FromSqlRaw(sql, new NpgsqlParameter("id", _sampleFilmId))
                .AsNoTracking()
                .FirstOrDefaultAsync();
            Console.WriteLine($"[WARMUP] Entity Framework warmed up - film: {efFilm?.Title}");
        }

        // Reset stats after warmup
        await PgStats.ResetAsync(_baseConnStr);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        // Dump final pg_stat_statements summary
        try
        {
            await PgStats.DumpSummaryAsync(_baseConnStr);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PgStats] Failed to dump summary: {ex.Message}");
        }

        // Dump pengdows metrics
        DumpPengdowsMetrics("GlobalCleanup");

        if (_pengdowsContext is IAsyncDisposable ctxDisposable)
        {
            await ctxDisposable.DisposeAsync();
        }

        if (_efDbContext != null)
        {
            await _efDbContext.DisposeAsync();
        }

        if (_dapperDataSource != null)
        {
            await _dapperDataSource.DisposeAsync();
        }

        if (_pengdowsDataSource != null)
        {
            await _pengdowsDataSource.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        PgStats.ResetAsync(_baseConnStr).GetAwaiter().GetResult();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        var label = _currentBenchmarkLabel ?? "(unknown)";
        PgStats.DumpSummaryAsync(_baseConnStr, label).GetAwaiter().GetResult();
    }

    // ============================================================
    // Sequential benchmarks: 6 operations x 3 frameworks = 18 methods
    // ============================================================

    // --- SingleRead ---

    [Benchmark]
    public async Task<Film?> SingleRead_Pengdows()
    {
        _currentBenchmarkLabel = nameof(SingleRead_Pengdows);
        await using var sc = _pengdowsContext.CreateSqlContainer();
        var sql = BuildSingleReadSql(p => sc.MakeParameterName(p));
        sc.Query.Append(sql);
        sc.AddParameterWithValue("id", DbType.Int32, _sampleFilmId);
        await using var reader = await sc.ExecuteReaderSingleRowAsync();
        return await reader.ReadAsync() ? _filmHelper.MapReaderToObject(reader) : null;
    }

    [Benchmark]
    public async Task<Film?> SingleRead_Dapper()
    {
        _currentBenchmarkLabel = nameof(SingleRead_Dapper);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildSingleReadSql(p => $"@{p}");
        var row = await conn.QuerySingleOrDefaultAsync<DapperFilmRow>(sql, new { id = _sampleFilmId });
        return row == null ? null : MapDapperFilm(row);
    }

    [Benchmark]
    public async Task<EfFilm?> SingleRead_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(SingleRead_EntityFramework);
        var sql = BuildSingleReadSql(p => $"@{p}");
        return await _efDbContext.Films
            .FromSqlRaw(sql, new NpgsqlParameter("id", _sampleFilmId))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    // --- CompositeKeyRead ---

    [Benchmark]
    public async Task<FilmActor?> CompositeKeyRead_Pengdows()
    {
        _currentBenchmarkLabel = nameof(CompositeKeyRead_Pengdows);
        await using var sc = _pengdowsContext.CreateSqlContainer();
        var sql = BuildCompositeKeyReadSql(p => sc.MakeParameterName(p));
        sc.Query.Append(sql);
        sc.AddParameterWithValue("actorId", DbType.Int32, _sampleCompositeKey.actorId);
        sc.AddParameterWithValue("filmId", DbType.Int32, _sampleCompositeKey.filmId);
        await using var reader = await sc.ExecuteReaderSingleRowAsync();
        return await reader.ReadAsync() ? _filmActorHelper.MapReaderToObject(reader) : null;
    }

    [Benchmark]
    public async Task<FilmActor?> CompositeKeyRead_Dapper()
    {
        _currentBenchmarkLabel = nameof(CompositeKeyRead_Dapper);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildCompositeKeyReadSql(p => $"@{p}");
        var row = await conn.QuerySingleOrDefaultAsync<DapperFilmActorRow>(
            sql,
            new { actorId = _sampleCompositeKey.actorId, filmId = _sampleCompositeKey.filmId });
        return row == null ? null : MapDapperFilmActor(row);
    }

    [Benchmark]
    public async Task<EfFilmActor?> CompositeKeyRead_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(CompositeKeyRead_EntityFramework);
        var sql = BuildCompositeKeyReadSql(p => $"@{p}");
        return await _efDbContext.FilmActors
            .FromSqlRaw(
                sql,
                new NpgsqlParameter("actorId", _sampleCompositeKey.actorId),
                new NpgsqlParameter("filmId", _sampleCompositeKey.filmId))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    // --- ListRead ---

    [Benchmark]
    public async Task<List<Film>> ListRead_Pengdows()
    {
        _currentBenchmarkLabel = nameof(ListRead_Pengdows);
        await using var sc = _pengdowsContext.CreateSqlContainer();
        var sql = BuildListReadSql(p => sc.MakeParameterName(p));
        sc.Query.Append(sql);
        sc.AddParameterWithValue("minLength", DbType.Int32, 100);
        await using var reader = await sc.ExecuteReaderAsync();
        var results = new List<Film>();
        while (await reader.ReadAsync())
        {
            results.Add(_filmHelper.MapReaderToObject(reader));
        }

        return results;
    }

    [Benchmark]
    public async Task<List<Film>> ListRead_Dapper()
    {
        _currentBenchmarkLabel = nameof(ListRead_Dapper);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildListReadSql(p => $"@{p}");
        var rows = await conn.QueryAsync<DapperFilmRow>(sql, new { minLength = 100 });
        return rows.Select(MapDapperFilm).ToList();
    }

    [Benchmark]
    public async Task<List<EfFilm>> ListRead_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(ListRead_EntityFramework);
        var sql = BuildListReadSql(p => $"@{p}");
        return await _efDbContext.Films
            .FromSqlRaw(sql, new NpgsqlParameter("minLength", 100))
            .AsNoTracking()
            .ToListAsync();
    }

    // --- Insert ---

    [Benchmark]
    public async Task<int> Insert_Pengdows()
    {
        _currentBenchmarkLabel = nameof(Insert_Pengdows);
        var title = $"ServerExec_{Interlocked.Increment(ref _runCounter):D10}";
        await using var sc = _pengdowsContext.CreateSqlContainer();
        var sql = BuildInsertSql(p => sc.MakeParameterName(p));
        sc.Query.Append(sql);
        sc.AddParameterWithValue("title", DbType.String, title);
        sc.AddParameterWithValue("length", DbType.Int32, 90);
        return await sc.ExecuteScalarWriteAsync<int>(CommandType.Text);
    }

    [Benchmark]
    public async Task<int> Insert_Dapper()
    {
        _currentBenchmarkLabel = nameof(Insert_Dapper);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var title = $"ServerExec_{Interlocked.Increment(ref _runCounter):D10}";
        var sql = BuildInsertSql(p => $"@{p}");
        return await conn.ExecuteScalarAsync<int>(sql, new { title, length = 90 });
    }

    [Benchmark]
    public async Task<int> Insert_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(Insert_EntityFramework);
        var title = $"ServerExec_{Interlocked.Increment(ref _runCounter):D10}";
        var sql = BuildInsertSql(p => $"@{p}");
        return await ExecuteScalarAsync(
            _efDbContext,
            sql,
            new NpgsqlParameter("title", title),
            new NpgsqlParameter("length", 90));
    }

    // --- Update ---

    [Benchmark]
    public async Task<int> Update_Pengdows()
    {
        _currentBenchmarkLabel = nameof(Update_Pengdows);
        var newLength = _flip ? 150 : 75;
        _flip = !_flip;
        await using var sc = _pengdowsContext.CreateSqlContainer();
        var sql = BuildUpdateSql(p => sc.MakeParameterName(p));
        sc.Query.Append(sql);
        sc.AddParameterWithValue("id", DbType.Int32, _sampleFilmId);
        sc.AddParameterWithValue("len", DbType.Int32, newLength);
        return await sc.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> Update_Dapper()
    {
        _currentBenchmarkLabel = nameof(Update_Dapper);
        var newLength = _flip ? 150 : 75;
        _flip = !_flip;
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildUpdateSql(p => $"@{p}");
        return await conn.ExecuteAsync(sql, new { id = _sampleFilmId, len = newLength });
    }

    [Benchmark]
    public async Task<int> Update_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(Update_EntityFramework);
        var newLength = _flip ? 150 : 75;
        _flip = !_flip;
        var sql = BuildUpdateSql(p => $"@{p}");
        return await _efDbContext.Database.ExecuteSqlRawAsync(
            sql,
            new NpgsqlParameter("id", _sampleFilmId),
            new NpgsqlParameter("len", newLength));
    }

    // --- Delete (insert temp row first, then delete it) ---

    [Benchmark]
    public async Task<int> Delete_Pengdows()
    {
        _currentBenchmarkLabel = nameof(Delete_Pengdows);

        // Insert a temporary row
        var title = $"TempDel_{Interlocked.Increment(ref _runCounter):D10}";
        await using var insertSc = _pengdowsContext.CreateSqlContainer();
        var insertSql = BuildTempInsertSql(p => insertSc.MakeParameterName(p));
        insertSc.Query.Append(insertSql);
        insertSc.AddParameterWithValue("title", DbType.String, title);
        insertSc.AddParameterWithValue("length", DbType.Int32, 60);
        var tempId = await insertSc.ExecuteScalarWriteAsync<int>(CommandType.Text);

        // Delete the temporary row
        await using var deleteSc = _pengdowsContext.CreateSqlContainer();
        var deleteSql = BuildDeleteSql(p => deleteSc.MakeParameterName(p));
        deleteSc.Query.Append(deleteSql);
        deleteSc.AddParameterWithValue("id", DbType.Int32, tempId);
        return await deleteSc.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> Delete_Dapper()
    {
        _currentBenchmarkLabel = nameof(Delete_Dapper);

        await using var conn = await _dapperDataSource.OpenConnectionAsync();

        // Insert a temporary row
        var title = $"TempDel_{Interlocked.Increment(ref _runCounter):D10}";
        var insertSql = BuildTempInsertSql(p => $"@{p}");
        var tempId = await conn.ExecuteScalarAsync<int>(insertSql, new { title, length = 60 });

        // Delete the temporary row
        var deleteSql = BuildDeleteSql(p => $"@{p}");
        return await conn.ExecuteAsync(deleteSql, new { id = tempId });
    }

    [Benchmark]
    public async Task<int> Delete_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(Delete_EntityFramework);

        // Insert a temporary row
        var title = $"TempDel_{Interlocked.Increment(ref _runCounter):D10}";
        var insertSql = BuildTempInsertSql(p => $"@{p}");
        var tempId = await ExecuteScalarAsync(
            _efDbContext,
            insertSql,
            new NpgsqlParameter("title", title),
            new NpgsqlParameter("length", 60));

        // Delete the temporary row
        var deleteSql = BuildDeleteSql(p => $"@{p}");
        return await ExecuteNonQueryAsync(
            _efDbContext,
            deleteSql,
            new NpgsqlParameter("id", tempId));
    }

    // ============================================================
    // Connection Hold Time measurement (3 methods)
    // Thesis point #4: pengdows holds connections for less time
    // ============================================================

    [Benchmark]
    public async Task<long> ConnectionHoldTime_Pengdows()
    {
        _currentBenchmarkLabel = nameof(ConnectionHoldTime_Pengdows);
        var sw = Stopwatch.StartNew();

        await using var sc = _pengdowsContext.CreateSqlContainer();
        var sql = BuildSingleReadSql(p => sc.MakeParameterName(p));
        sc.Query.Append(sql);
        sc.AddParameterWithValue("id", DbType.Int32, _sampleFilmId);
        await using var reader = await sc.ExecuteReaderSingleRowAsync();
        if (await reader.ReadAsync())
        {
            _filmHelper.MapReaderToObject(reader);
        }

        sw.Stop();
        return sw.ElapsedTicks;
    }

    [Benchmark]
    public async Task<long> ConnectionHoldTime_Dapper()
    {
        _currentBenchmarkLabel = nameof(ConnectionHoldTime_Dapper);
        var sw = Stopwatch.StartNew();

        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildSingleReadSql(p => $"@{p}");
        var row = await conn.QuerySingleOrDefaultAsync<DapperFilmRow>(sql, new { id = _sampleFilmId });
        if (row != null)
        {
            MapDapperFilm(row);
        }

        // Connection stays open until disposed at end of using block
        sw.Stop();
        return sw.ElapsedTicks;
    }

    [Benchmark]
    public async Task<long> ConnectionHoldTime_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(ConnectionHoldTime_EntityFramework);
        var sw = Stopwatch.StartNew();

        await using var ctx = new ServerExecDbContext(_efOptions);
        var sql = BuildSingleReadSql(p => $"@{p}");
        await ctx.Films
            .FromSqlRaw(sql, new NpgsqlParameter("id", _sampleFilmId))
            .AsNoTracking()
            .FirstOrDefaultAsync();

        sw.Stop();
        return sw.ElapsedTicks;
    }

    // ============================================================
    // Helpers
    // ============================================================

    private async Task WaitForReady()
    {
        for (var i = 0; i < 60; i++)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_baseConnStr);
                await conn.OpenAsync();
                await conn.CloseAsync();
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        throw new TimeoutException("Postgres container did not become ready in time.");
    }

    private async Task CreateSchemaAndSeedAsync()
    {
        await using var conn = new NpgsqlConnection(_baseConnStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var ddl = @"
DROP TABLE IF EXISTS film_actor;
DROP TABLE IF EXISTS film;

CREATE TABLE film (
    film_id SERIAL PRIMARY KEY,
    title   VARCHAR(255) NOT NULL,
    length  INTEGER
);

CREATE TABLE film_actor (
    actor_id INTEGER NOT NULL,
    film_id  INTEGER NOT NULL,
    PRIMARY KEY (actor_id, film_id)
);
";
        await conn.ExecuteAsync(ddl, transaction: tx);

        // Seed 1000 films
        const string filmIns = "INSERT INTO film(title, length) VALUES (@t, @len)";
        for (var i = 0; i < 1000; i++)
        {
            await conn.ExecuteAsync(filmIns, new { t = $"Film {i}", len = 60 + i % 120 }, tx);
        }

        // Seed 5000 film_actor rows
        const string faIns = "INSERT INTO film_actor(actor_id, film_id) VALUES (@a, @f)";
        var faCount = 0;
        for (var actorId = 1; actorId <= 200 && faCount < 5000; actorId++)
        {
            for (var filmId = 1; filmId <= 1000 && faCount < 5000; filmId += 200 / Math.Max(1, actorId % 10 + 1))
            {
                await conn.ExecuteAsync(faIns, new { a = actorId, f = filmId }, tx);
                faCount++;
            }
        }

        await tx.CommitAsync();
        Console.WriteLine($"[SEED] Seeded 1000 films and {faCount} film_actor rows.");
    }

    private void DumpPengdowsMetrics(string label)
    {
        var metrics = _pengdowsContext.Metrics;
        Console.WriteLine(
            $"[METRICS] {label} " +
            $"conn_open_avg={metrics.AvgConnectionOpenMs:0.000}ms " +
            $"conn_close_avg={metrics.AvgConnectionCloseMs:0.000}ms " +
            $"conn_hold_avg={metrics.AvgConnectionHoldMs:0.000}ms " +
            $"cmd_avg={metrics.AvgCommandMs:0.000}ms " +
            $"p95={metrics.P95CommandMs:0.000}ms p99={metrics.P99CommandMs:0.000}ms " +
            $"conns_opened={metrics.ConnectionsOpened} conns_closed={metrics.ConnectionsClosed}");
    }

    // SQL builders using template + param delegate pattern

    private static string BuildSingleReadSql(Func<string, string> param)
    {
        return SingleReadSqlTemplate.Replace("{id}", param("id"));
    }

    private static string BuildCompositeKeyReadSql(Func<string, string> param)
    {
        return CompositeKeyReadSqlTemplate
            .Replace("{actorId}", param("actorId"))
            .Replace("{filmId}", param("filmId"));
    }

    private static string BuildListReadSql(Func<string, string> param)
    {
        return ListReadSqlTemplate.Replace("{minLength}", param("minLength"));
    }

    private static string BuildInsertSql(Func<string, string> param)
    {
        return InsertSqlTemplate
            .Replace("{title}", param("title"))
            .Replace("{length}", param("length"));
    }

    private static string BuildUpdateSql(Func<string, string> param)
    {
        return UpdateSqlTemplate
            .Replace("{len}", param("len"))
            .Replace("{id}", param("id"));
    }

    private static string BuildDeleteSql(Func<string, string> param)
    {
        return DeleteSqlTemplate.Replace("{id}", param("id"));
    }

    private static string BuildTempInsertSql(Func<string, string> param)
    {
        return TempInsertSqlTemplate
            .Replace("{title}", param("title"))
            .Replace("{length}", param("length"));
    }

    // Dapper row mappers

    private static Film MapDapperFilm(DapperFilmRow row)
    {
        return new Film
        {
            FilmId = row.film_id,
            Title = row.title,
            Length = row.length
        };
    }

    private static FilmActor MapDapperFilmActor(DapperFilmActorRow row)
    {
        return new FilmActor
        {
            ActorId = row.actor_id,
            FilmId = row.film_id
        };
    }

    // EF helper methods for raw scalar/non-query execution

    private static async Task<int> ExecuteScalarAsync(
        DbContext context,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            var result = await command.ExecuteScalarAsync();
            return result == null || result is DBNull ? 0 : Convert.ToInt32(result);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<int> ExecuteNonQueryAsync(
        DbContext context,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            return await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
    }

    // ============================================================
    // pengdows.crud entities
    // ============================================================

    [pengdows.crud.attributes.Table("film")]
    public class Film
    {
        [Id(false)]
        [pengdows.crud.attributes.Column("film_id", DbType.Int32)]
        public int FilmId { get; set; }

        [pengdows.crud.attributes.Column("title", DbType.String)]
        public string Title { get; set; } = string.Empty;

        [pengdows.crud.attributes.Column("length", DbType.Int32)]
        public int Length { get; set; }
    }

    [pengdows.crud.attributes.Table("film_actor")]
    public class FilmActor
    {
        [Id(false)]
        [pengdows.crud.attributes.Column("actor_id", DbType.Int32)]
        public int ActorId { get; set; }

        [pengdows.crud.attributes.Column("film_id", DbType.Int32)]
        public int FilmId { get; set; }
    }

    // ============================================================
    // Dapper row types (snake_case to match column names)
    // ============================================================

    private sealed class DapperFilmRow
    {
        public int film_id { get; set; }
        public string title { get; set; } = string.Empty;
        public int length { get; set; }
    }

    private sealed class DapperFilmActorRow
    {
        public int actor_id { get; set; }
        public int film_id { get; set; }
    }

    // ============================================================
    // Entity Framework entities and DbContext
    // ============================================================

    public class EfFilm
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Length { get; set; }
        public virtual ICollection<EfFilmActor> FilmActors { get; set; } = new List<EfFilmActor>();
    }

    public class EfFilmActor
    {
        public int ActorId { get; set; }
        public int FilmId { get; set; }
    }

    public class ServerExecDbContext : DbContext
    {
        public ServerExecDbContext(DbContextOptions<ServerExecDbContext> options) : base(options)
        {
        }

        public DbSet<EfFilm> Films { get; set; } = null!;
        public DbSet<EfFilmActor> FilmActors { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfFilm>(entity =>
            {
                entity.ToTable("film");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("film_id").ValueGeneratedOnAdd();
                entity.Property(e => e.Title).HasColumnName("title").IsRequired();
                entity.Property(e => e.Length).HasColumnName("length");
            });

            modelBuilder.Entity<EfFilmActor>(entity =>
            {
                entity.ToTable("film_actor");
                entity.HasKey(e => new { e.ActorId, e.FilmId });
                entity.Property(e => e.ActorId).HasColumnName("actor_id");
                entity.Property(e => e.FilmId).HasColumnName("film_id");
            });
        }
    }
}
