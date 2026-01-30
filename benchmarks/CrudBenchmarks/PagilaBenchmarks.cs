using System.Data;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// pengdows.crud Parameter Naming Reference:
/// - RETRIEVE operations: w0, w1, w2... (WHERE parameters)
/// - UPDATE operations: s0, s1, s2... (SET parameters), w0, w1... (WHERE parameters)
/// - CREATE operations: i0, i1, i2... (INSERT parameters)
/// - DELETE operations: w0, w1, w2... (WHERE parameters)
/// See: /docs/parameter-naming-convention.md
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 10, invocationCount: 100)]
public class PagilaBenchmarks : IAsyncDisposable
{
    private const string FilmByIdSqlTemplate = """
        select film_id, title, length
        from film
        where film_id = {id}
        """;

    private const string FilmActorCompositeSqlTemplate = """
        select actor_id, film_id
        from film_actor
        where actor_id = {actorId} and film_id = {filmId}
        """;

    private const string FilmLengthByIdSqlTemplate = """
        select length
        from film
        where film_id = {id}
        """;

    private const string FilmUpdateLengthSqlTemplate = """
        update film
        set length = {len}
        where film_id = {id}
        """;

    private const string FilmInsertSqlTemplate = """
        insert into film(title, length)
        values ({title}, {length})
        returning film_id
        """;

    private const string FilmDeleteSqlTemplate = """
        delete from film
        where film_id = {id}
        """;

    private const string FilmIdsSqlTemplate = """
        select film_id, title, length
        from film
        where film_id = any({ids})
        """;
    private IContainer? _container;
    private string _baseConnStr = string.Empty;
    private string _pengdowsConnStr = string.Empty;
    private string _efConnStr = string.Empty;
    private string _efSettingsConnStr = string.Empty;
    private string _dapperConnStr = string.Empty;
    private string _dapperSettingsConnStr = string.Empty;
    private IDatabaseContext _ctx = null!;
    private ILoggerFactory? _loggerFactory;
    private TypeMapRegistry _map = null!;
    private TableGateway<Film, int> _filmHelper = null!;
    private TableGateway<FilmActor, int> _filmActorHelper = null!;
    private PagilaDbContext _efDbContext = null!;
    private PagilaDbContext _efDbContextWithSettings = null!;
    private DbContextOptions<PagilaDbContext> _efOptions = null!;
    private DbContextOptions<PagilaDbContext> _efOptionsWithSettings = null!;
    private NpgsqlDataSource _dapperDataSource = null!;
    private NpgsqlDataSource _dapperSettingsDataSource = null!;
    // Remove manual template caching - use TableGateway's built-in caching

    [Params(1000)] public int FilmCount;

    [Params(200)] public int ActorCount;
    [Params(16)] public int Parallelism;
    [Params(64)] public int OperationsPerRun;

    private int _filmId;
    private List<int> _filmIds10 = new();
    private bool _flip;
    private (int actorId, int filmId) _compositeKey;
    private long _runCounter;
    [ThreadStatic] private static string? _currentBenchmarkLabel;
    private bool _collectPerIteration = true; // Enable per-iteration pg_stat_statements for DB time analysis
    private bool _timingEnabled;
    private long _breakdownBuildTicks;
    private long _breakdownExecuteTicks;
    private long _breakdownMapTicks;
    private int _breakdownOps;
    private long _dapperBreakdownBuildTicks;
    private long _dapperBreakdownExecuteTicks;
    private long _dapperBreakdownMapTicks;
    private int _dapperBreakdownOps;
    private long _efBreakdownBuildTicks;
    private long _efBreakdownExecuteTicks;
    private long _efBreakdownMapTicks;
    private int _efBreakdownOps;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithEnvironment("POSTGRES_DB", "pagila")
            .WithPortBinding(0, 5432)
            // Enable pg_stat_statements and I/O timing for richer statistics
            .WithCommand(
                "postgres",
                "-c", "shared_preload_libraries=pg_stat_statements",
                "-c", "pg_stat_statements.track=all",
                "-c", "track_io_timing=on")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _container.StartAsync();

        var mappedPort = _container.GetMappedPublicPort(5432);

        // Use different Application Names to ensure separate connection pools for each library
        // This ensures fair benchmarking without cross-pollination of session states
        _baseConnStr =
            $"Host=localhost;Port={mappedPort};Database=pagila;Username=postgres;Password=postgres;";
        _pengdowsConnStr = _baseConnStr + "Application Name=Benchmark_PengdowsCrud;";
        _efConnStr = _baseConnStr + "Application Name=Benchmark_EntityFramework;";
        _efSettingsConnStr = _baseConnStr + "Application Name=Benchmark_EntityFramework_Settings;";
        _dapperConnStr = _baseConnStr + "Application Name=Benchmark_Dapper;";
        _dapperSettingsConnStr = _baseConnStr + "Application Name=Benchmark_Dapper_Settings;";

        await WaitForReady();
        await CreateSchemaAndSeedAsync();

        // Initialize pg_stat_statements and reset stats before running benchmarks
        await using (var admin = new NpgsqlConnection(_baseConnStr))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS pg_stat_statements;");
            await admin.ExecuteAsync("SELECT pg_stat_statements_reset();");
        }

        _map = new TypeMapRegistry();
        _map.Register<Film>();
        _map.Register<FilmActor>();
        // Use Standard mode for fair comparison with Dapper's ephemeral connections
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _pengdowsConnStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _timingEnabled = IsTimingEnabled();
        if (_timingEnabled)
        {
            _loggerFactory = new LoggerFactory(new ILoggerProvider[]
            {
                new SimpleConsoleLoggerProvider(LogLevel.Debug)
            });
        }

        _ctx = new DatabaseContext(cfg, NpgsqlFactory.Instance, _loggerFactory, _map);

        // Verify the actual mode being used
        Console.WriteLine($"[BENCHMARK] Configured DbMode: {cfg.DbMode}");
        Console.WriteLine($"[BENCHMARK] Actual ConnectionMode: {_ctx.ConnectionMode}");

        _filmHelper = new TableGateway<Film, int>(_ctx);
        _filmActorHelper = new TableGateway<FilmActor, int>(_ctx);

        // Initialize Entity Framework DbContext with its own connection pool
        _efOptions = new DbContextOptionsBuilder<PagilaDbContext>()
            .UseNpgsql(_efConnStr)
            .Options;
        _efDbContext = new PagilaDbContext(_efOptions);

        // Initialize Entity Framework DbContext with session settings parity
        _efOptionsWithSettings = new DbContextOptionsBuilder<PagilaDbContext>()
            .UseNpgsql(_efSettingsConnStr)
            .AddInterceptors(new SessionSettingsConnectionInterceptor(BenchmarkSessionSettings.PostgresSessionSettings))
            .Options;
        _efDbContextWithSettings = new PagilaDbContext(_efOptionsWithSettings);

        // Initialize Dapper data source with its own connection pool
        _dapperDataSource = NpgsqlDataSource.Create(_dapperConnStr);
        _dapperSettingsDataSource = NpgsqlDataSource.Create(_dapperSettingsConnStr);

        // TableGateway will handle internal caching and cloning automatically

        // pick keys to use in benchmarks
        await using var conn = new NpgsqlConnection(_baseConnStr);
        await conn.OpenAsync();
        _filmId = await conn.ExecuteScalarAsync<int>("select film_id from film order by film_id limit 1");
        var row = await conn.QuerySingleAsync<(int actor_id, int film_id)>(
            "select actor_id, film_id from film_actor limit 1");
        _compositeKey = (row.actor_id, row.film_id);
        _filmIds10 = (await conn.QueryAsync<int>("select film_id from film order by film_id limit 10")).ToList();

        // Warmup both systems to ensure fair comparison
        Console.WriteLine("[WARMUP] Warming up pengdows.crud...");
        await using (var warmupContainer = _ctx.CreateSqlContainer())
        {
            var warmupSql = BuildFilmByIdSql(param => warmupContainer.MakeParameterName(param));
            warmupContainer.Query.Append(warmupSql);
            warmupContainer.AddParameterWithValue("id", DbType.Int32, _filmId);
            await using var warmupReader = await warmupContainer.ExecuteReaderSingleRowAsync();
            Film? warmupFilm = null;
            if (await warmupReader.ReadAsync())
            {
                warmupFilm = _filmHelper.MapReaderToObject(warmupReader);
            }

            Console.WriteLine($"[WARMUP] pengdows.crud warmed up - retrieved film: {warmupFilm?.Title}");
        }

        Console.WriteLine("[WARMUP] Warming up Dapper...");
        await using var warmupConn = await _dapperDataSource.OpenConnectionAsync();
        var dapperWarmupSql = BuildFilmByIdSql(param => $"@{param}");
        var dapperWarmupRow = await warmupConn.QuerySingleOrDefaultAsync<DapperFilmRow>(
            dapperWarmupSql,
            new { id = _filmId });
        var dapperWarmup = dapperWarmupRow == null ? null : MapFilm(dapperWarmupRow);
        Console.WriteLine($"[WARMUP] Dapper warmed up - retrieved film: {dapperWarmup?.Title}");

        Console.WriteLine("[WARMUP] Warming up Entity Framework...");
        var efWarmupSql = BuildFilmByIdSql(param => $"@{param}");
        var efWarmup = await _efDbContext.Films
            .FromSqlRaw(efWarmupSql, new NpgsqlParameter("id", _filmId))
            .FirstOrDefaultAsync();
        Console.WriteLine($"[WARMUP] Entity Framework warmed up - retrieved film: {efWarmup?.Title}");
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_ctx is IAsyncDisposable ad)
        {
            await ad.DisposeAsync();
        }

        if (_efDbContext != null)
        {
            await _efDbContext.DisposeAsync();
        }

        if (_efDbContextWithSettings != null)
        {
            await _efDbContextWithSettings.DisposeAsync();
        }

        if (_dapperDataSource != null)
        {
            await _dapperDataSource.DisposeAsync();
        }

        if (_dapperSettingsDataSource != null)
        {
            await _dapperSettingsDataSource.DisposeAsync();
        }

        _loggerFactory?.Dispose();

        // Dump Postgres statistics for analysis
        try
        {
            await PgStats.DumpSummaryAsync(_baseConnStr);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PgStats] Failed to dump summary: {ex.Message}");
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
        if (_collectPerIteration)
        {
            PgStats.ResetAsync(_baseConnStr).GetAwaiter().GetResult();
        }

        _breakdownBuildTicks = 0;
        _breakdownExecuteTicks = 0;
        _breakdownMapTicks = 0;
        _breakdownOps = 0;
        _dapperBreakdownBuildTicks = 0;
        _dapperBreakdownExecuteTicks = 0;
        _dapperBreakdownMapTicks = 0;
        _dapperBreakdownOps = 0;
        _efBreakdownBuildTicks = 0;
        _efBreakdownExecuteTicks = 0;
        _efBreakdownMapTicks = 0;
        _efBreakdownOps = 0;
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        if (_collectPerIteration)
        {
            var label = _currentBenchmarkLabel ?? "(unknown)";
            PgStats.DumpSummaryAsync(_baseConnStr, label).GetAwaiter().GetResult();
            if (label.Contains("_Mine", StringComparison.OrdinalIgnoreCase))
            {
                DumpPengdowsMetrics(label);
            }

            if (label == nameof(GetFilmById_Mine_Breakdown))
            {
                DumpBreakdownMetrics(label, _breakdownBuildTicks, _breakdownExecuteTicks, _breakdownMapTicks,
                    _breakdownOps);
            }

            if (label == nameof(GetFilmById_Dapper_Breakdown))
            {
                DumpBreakdownMetrics(label, _dapperBreakdownBuildTicks, _dapperBreakdownExecuteTicks,
                    _dapperBreakdownMapTicks, _dapperBreakdownOps);
            }

            if (label == nameof(GetFilmById_EntityFramework_NoTracking_Breakdown))
            {
                DumpBreakdownMetrics(label, _efBreakdownBuildTicks, _efBreakdownExecuteTicks,
                    _efBreakdownMapTicks, _efBreakdownOps);
            }
        }
    }

    private async Task WaitForReady()
    {
        // Simple retry loop until the DB accepts connections
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

        // Minimal Pagila-like subset
        var sql = @"
DROP TABLE IF EXISTS film_actor;
DROP TABLE IF EXISTS film;
DROP TABLE IF EXISTS actor;

CREATE TABLE actor (
  actor_id   SERIAL PRIMARY KEY,
  first_name TEXT NOT NULL,
  last_name  TEXT NOT NULL
);

CREATE TABLE film (
  film_id SERIAL PRIMARY KEY,
  title   TEXT NOT NULL,
  length  INT  NOT NULL
);

CREATE TABLE film_actor (
  actor_id INT NOT NULL REFERENCES actor(actor_id) ON DELETE CASCADE,
  film_id  INT NOT NULL REFERENCES film(film_id)  ON DELETE CASCADE,
  PRIMARY KEY (actor_id, film_id)
);
";
        await conn.ExecuteAsync(sql, transaction: tx);

        // Seed actors
        {
            const string ins = "insert into actor(first_name, last_name) values (@f, @l)";
            for (var i = 0; i < ActorCount; i++)
            {
                await conn.ExecuteAsync(ins, new { f = $"A{i}", l = $"L{i}" }, tx);
            }
        }

        // Seed films
        {
            const string ins = "insert into film(title, length) values (@t, @len)";
            for (var i = 0; i < FilmCount; i++)
            {
                await conn.ExecuteAsync(ins, new { t = $"Film {i}", len = 60 + i % 120 }, tx);
            }
        }

        // Seed film_actor associations (simple round-robin)
        {
            const string ins = "insert into film_actor(actor_id, film_id) values (@a, @f)";
            for (var i = 1; i <= ActorCount; i++)
            {
                for (var f = i; f <= FilmCount; f += Math.Max(1, FilmCount / 50)) // ~50 films per actor
                {
                    await conn.ExecuteAsync(ins, new { a = i, f }, tx);
                }
            }
        }

        await tx.CommitAsync();
    }

    private void DumpPengdowsMetrics(string label)
    {
        var metrics = _ctx.Metrics;
        Console.WriteLine(
            $"[METRICS] {label} conn_open_avg={metrics.AvgConnectionOpenMs:0.000}ms " +
            $"conn_close_avg={metrics.AvgConnectionCloseMs:0.000}ms " +
            $"conn_hold_avg={metrics.AvgConnectionHoldMs:0.000}ms " +
            $"cmd_avg={metrics.AvgCommandMs:0.000}ms " +
            $"p95={metrics.P95CommandMs:0.000}ms p99={metrics.P99CommandMs:0.000}ms");
    }

    private static bool IsTimingEnabled()
    {
        var value = Environment.GetEnvironmentVariable("PENGDOWS_SQL_TIMING");
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SimpleConsoleLoggerProvider : ILoggerProvider
    {
        private readonly LogLevel _minLevel;

        public SimpleConsoleLoggerProvider(LogLevel minLevel)
        {
            _minLevel = minLevel;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new SimpleConsoleLogger(categoryName, _minLevel);
        }

        public void Dispose()
        {
        }
    }

    private sealed class SimpleConsoleLogger : ILogger
    {
        private readonly string _category;
        private readonly LogLevel _minLevel;

        public SimpleConsoleLogger(string category, LogLevel minLevel)
        {
            _category = category;
            _minLevel = minLevel;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NoopScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= _minLevel;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Console.WriteLine($"[LOG] {_category} {logLevel}: {message}");
            if (exception != null)
            {
                Console.WriteLine(exception);
            }
        }

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private void DumpBreakdownMetrics(string label, long buildTicks, long executeTicks, long mapTicks, int ops)
    {
        if (ops == 0)
        {
            return;
        }

        var scale = 1000d / Stopwatch.Frequency;
        var buildUs = buildTicks / (double)ops * scale;
        var execUs = executeTicks / (double)ops * scale;
        var mapUs = mapTicks / (double)ops * scale;

        Console.WriteLine(
            $"[BREAKDOWN] {label} build={buildUs:0.000}us execute={execUs:0.000}us map={mapUs:0.000}us");
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_Mine()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_Mine);
        await using var container = _ctx.CreateSqlContainer();
        var sql = BuildFilmByIdSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("id", DbType.Int32, _filmId);

        await using var reader = await container.ExecuteReaderSingleRowAsync();
        return await reader.ReadAsync() ? _filmHelper.MapReaderToObject(reader) : null;
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_Mine_Breakdown()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_Mine_Breakdown);

        var t0 = Stopwatch.GetTimestamp();
        await using var sc = _ctx.CreateSqlContainer();
        var sql = BuildFilmByIdSql(param => sc.MakeParameterName(param));
        sc.Query.Append(sql);
        sc.AddParameterWithValue("id", DbType.Int32, _filmId);
        var t1 = Stopwatch.GetTimestamp();
        await using var reader = await sc.ExecuteReaderSingleRowAsync();
        var t2 = Stopwatch.GetTimestamp();

        Film? result = null;
        if (await reader.ReadAsync())
        {
            result = _filmHelper.MapReaderToObject(reader);
        }

        var t3 = Stopwatch.GetTimestamp();
        _breakdownBuildTicks += t1 - t0;
        _breakdownExecuteTicks += t2 - t1;
        _breakdownMapTicks += t3 - t2;
        _breakdownOps++;

        return result;
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_Dapper_Breakdown()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_Dapper_Breakdown);

        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var t0 = Stopwatch.GetTimestamp();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildFilmByIdSql(param => $"@{param}");
        var param = cmd.CreateParameter();
        param.ParameterName = "id";
        param.Value = _filmId;
        cmd.Parameters.Add(param);
        var t1 = Stopwatch.GetTimestamp();

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
        var t2 = Stopwatch.GetTimestamp();

        Film? result = null;
        if (await reader.ReadAsync())
        {
            var parser = SqlMapper.GetRowParser<DapperFilmRow>(reader);
            var row = parser(reader);
            result = MapFilm(row);
        }

        var t3 = Stopwatch.GetTimestamp();

        _dapperBreakdownBuildTicks += t1 - t0;
        _dapperBreakdownExecuteTicks += t2 - t1;
        _dapperBreakdownMapTicks += t3 - t2;
        _dapperBreakdownOps++;

        return result;
    }

    [Benchmark]
    public async Task<EfFilm?> GetFilmById_EntityFramework_NoTracking_Breakdown()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_EntityFramework_NoTracking_Breakdown);

        var t0 = Stopwatch.GetTimestamp();
        var sql = BuildFilmByIdSql(param => $"@{param}");
        var query = _efDbContext.Films
            .FromSqlRaw(sql, new NpgsqlParameter("id", _filmId))
            .AsNoTracking();
        var t1 = Stopwatch.GetTimestamp();

        var result = await query.FirstOrDefaultAsync();
        var t2 = Stopwatch.GetTimestamp();

        _efBreakdownBuildTicks += t1 - t0;
        _efBreakdownExecuteTicks += t2 - t1;
        _efBreakdownMapTicks += 0;
        _efBreakdownOps++;

        return result;
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_Dapper()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_Dapper);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildFilmByIdSql(param => $"@{param}");
        var row = await conn.QuerySingleOrDefaultAsync<DapperFilmRow>(sql, new { id = _filmId });
        return row == null ? null : MapFilm(row);
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_Dapper_WithSessionSettings()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_Dapper_WithSessionSettings);
        await using var conn = await _dapperSettingsDataSource.OpenConnectionAsync();
        await BenchmarkSessionSettings.ApplyAsync(conn, BenchmarkSessionSettings.PostgresSessionSettings);
        var sql = BuildFilmByIdSql(param => $"@{param}");
        var row = await conn.QuerySingleOrDefaultAsync<DapperFilmRow>(sql, new { id = _filmId });
        return row == null ? null : MapFilm(row);
    }

    [Benchmark]
    public async Task<FilmActor?> GetFilmActorComposite_Mine()
    {
        _currentBenchmarkLabel = nameof(GetFilmActorComposite_Mine);
        await using var container = _ctx.CreateSqlContainer();
        var sql = BuildFilmActorCompositeSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("actorId", DbType.Int32, _compositeKey.actorId);
        container.AddParameterWithValue("filmId", DbType.Int32, _compositeKey.filmId);

        await using var reader = await container.ExecuteReaderSingleRowAsync();
        return await reader.ReadAsync() ? _filmActorHelper.MapReaderToObject(reader) : null;
    }

    [Benchmark]
    public async Task<FilmActor?> GetFilmActorComposite_Dapper()
    {
        _currentBenchmarkLabel = nameof(GetFilmActorComposite_Dapper);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildFilmActorCompositeSql(param => $"@{param}");
        var row = await conn.QuerySingleOrDefaultAsync<DapperFilmActorRow>(
            sql,
            new { actorId = _compositeKey.actorId, filmId = _compositeKey.filmId });
        return row == null ? null : MapFilmActor(row);
    }

    [Benchmark]
    public async Task<FilmActor?> GetFilmActorComposite_Dapper_WithSessionSettings()
    {
        _currentBenchmarkLabel = nameof(GetFilmActorComposite_Dapper_WithSessionSettings);
        await using var conn = await _dapperSettingsDataSource.OpenConnectionAsync();
        await BenchmarkSessionSettings.ApplyAsync(conn, BenchmarkSessionSettings.PostgresSessionSettings);
        var sql = BuildFilmActorCompositeSql(param => $"@{param}");
        var row = await conn.QuerySingleOrDefaultAsync<DapperFilmActorRow>(
            sql,
            new { actorId = _compositeKey.actorId, filmId = _compositeKey.filmId });
        return row == null ? null : MapFilmActor(row);
    }

    [Benchmark]
    public async Task<int> UpdateFilm_Mine()
    {
        _currentBenchmarkLabel = nameof(UpdateFilm_Mine);
        await using var lengthContainer = _ctx.CreateSqlContainer();
        var lengthSql = BuildFilmLengthByIdSql(param => lengthContainer.MakeParameterName(param));
        lengthContainer.Query.Append(lengthSql);
        lengthContainer.AddParameterWithValue("id", DbType.Int32, _filmId);
        var currentLength = await lengthContainer.ExecuteScalarAsync<int>(CommandType.Text);

        var newLength = _flip ? currentLength + 1 : currentLength - 1;
        _flip = !_flip;

        await using var updateContainer = _ctx.CreateSqlContainer();
        var updateSql = BuildFilmUpdateLengthSql(param => updateContainer.MakeParameterName(param));
        updateContainer.Query.Append(updateSql);
        updateContainer.AddParameterWithValue("id", DbType.Int32, _filmId);
        updateContainer.AddParameterWithValue("len", DbType.Int32, newLength);
        return await updateContainer.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> UpdateFilm_Dapper()
    {
        _currentBenchmarkLabel = nameof(UpdateFilm_Dapper);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var lengthSql = BuildFilmLengthByIdSql(param => $"@{param}");
        var len = await conn.ExecuteScalarAsync<int>(lengthSql, new { id = _filmId });
        var newLen = _flip ? len + 1 : len - 1;
        _flip = !_flip;
        var updateSql = BuildFilmUpdateLengthSql(param => $"@{param}");
        return await conn.ExecuteAsync(updateSql, new { id = _filmId, len = newLen });
    }

    [Benchmark]
    public async Task<int> UpdateFilm_Dapper_WithSessionSettings()
    {
        _currentBenchmarkLabel = nameof(UpdateFilm_Dapper_WithSessionSettings);
        await using var conn = await _dapperSettingsDataSource.OpenConnectionAsync();
        await BenchmarkSessionSettings.ApplyAsync(conn, BenchmarkSessionSettings.PostgresSessionSettings);
        var lengthSql = BuildFilmLengthByIdSql(param => $"@{param}");
        var len = await conn.ExecuteScalarAsync<int>(lengthSql, new { id = _filmId });
        var newLen = _flip ? len + 1 : len - 1;
        _flip = !_flip;
        var updateSql = BuildFilmUpdateLengthSql(param => $"@{param}");
        return await conn.ExecuteAsync(updateSql, new { id = _filmId, len = newLen });
    }

    [Benchmark]
    public async Task<int> InsertThenDeleteFilm_Mine()
    {
        _currentBenchmarkLabel = nameof(InsertThenDeleteFilm_Mine);
        var title = $"Bench_{Interlocked.Increment(ref _runCounter):D10}";

        await using var insertContainer = _ctx.CreateSqlContainer();
        var insertSql = BuildFilmInsertSql(param => insertContainer.MakeParameterName(param));
        insertContainer.Query.Append(insertSql);
        insertContainer.AddParameterWithValue("title", DbType.String, title);
        insertContainer.AddParameterWithValue("length", DbType.Int32, 123);
        var id = await insertContainer.ExecuteScalarAsync<int>(CommandType.Text);

        await using var deleteContainer = _ctx.CreateSqlContainer();
        var deleteSql = BuildFilmDeleteSql(param => deleteContainer.MakeParameterName(param));
        deleteContainer.Query.Append(deleteSql);
        deleteContainer.AddParameterWithValue("id", DbType.Int32, id);
        return await deleteContainer.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> InsertThenDeleteFilm_Dapper()
    {
        _currentBenchmarkLabel = nameof(InsertThenDeleteFilm_Dapper);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var title = $"Bench_{Guid.NewGuid():N}";
        var insertSql = BuildFilmInsertSql(param => $"@{param}");
        var id = await conn.ExecuteScalarAsync<int>(insertSql, new { title, length = 123 });
        var deleteSql = BuildFilmDeleteSql(param => $"@{param}");
        return await conn.ExecuteAsync(deleteSql, new { id });
    }

    [Benchmark]
    public async Task<int> InsertThenDeleteFilm_Dapper_WithSessionSettings()
    {
        _currentBenchmarkLabel = nameof(InsertThenDeleteFilm_Dapper_WithSessionSettings);
        await using var conn = await _dapperSettingsDataSource.OpenConnectionAsync();
        await BenchmarkSessionSettings.ApplyAsync(conn, BenchmarkSessionSettings.PostgresSessionSettings);
        var title = $"Bench_{Guid.NewGuid():N}";
        var insertSql = BuildFilmInsertSql(param => $"@{param}");
        var id = await conn.ExecuteScalarAsync<int>(insertSql, new { title, length = 123 });
        var deleteSql = BuildFilmDeleteSql(param => $"@{param}");
        return await conn.ExecuteAsync(deleteSql, new { id });
    }


    [Benchmark]
    public async Task<List<Film>> GetTenFilms_Mine()
    {
        await using var container = _ctx.CreateSqlContainer();
        var sql = BuildFilmIdsSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("ids", DbType.Object, _filmIds10.ToArray());

        await using var reader = await container.ExecuteReaderAsync();
        var results = new List<Film>();
        while (await reader.ReadAsync())
        {
            results.Add(_filmHelper.MapReaderToObject(reader));
        }

        return results;
    }

    [Benchmark]
    public async Task<List<Film>> GetTenFilms_Dapper()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildFilmIdsSql(param => $"@{param}");
        var rows = await conn.QueryAsync<DapperFilmRow>(sql, new { ids = _filmIds10.ToArray() });
        return rows.Select(MapFilm).ToList();
    }

    [Benchmark]
    public async Task<List<Film>> GetTenFilms_Dapper_WithSessionSettings()
    {
        await using var conn = await _dapperSettingsDataSource.OpenConnectionAsync();
        await BenchmarkSessionSettings.ApplyAsync(conn, BenchmarkSessionSettings.PostgresSessionSettings);
        var sql = BuildFilmIdsSql(param => $"@{param}");
        var rows = await conn.QueryAsync<DapperFilmRow>(sql, new { ids = _filmIds10.ToArray() });
        return rows.Select(MapFilm).ToList();
    }

    // Entity Framework Benchmarks
    [Benchmark]
    public async Task<EfFilm?> GetFilmById_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_EntityFramework);
        var sql = BuildFilmByIdSql(param => $"@{param}");
        return await _efDbContext.Films
            .FromSqlRaw(sql, new NpgsqlParameter("id", _filmId))
            .FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<EfFilm?> GetFilmById_EntityFramework_WithSessionSettings()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_EntityFramework_WithSessionSettings);
        var sql = BuildFilmByIdSql(param => $"@{param}");
        return await _efDbContextWithSettings.Films
            .FromSqlRaw(sql, new NpgsqlParameter("id", _filmId))
            .FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<EfFilm?> GetFilmById_EntityFramework_NoTracking()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_EntityFramework_NoTracking);
        var sql = BuildFilmByIdSql(param => $"@{param}");
        return await _efDbContext.Films
            .FromSqlRaw(sql, new NpgsqlParameter("id", _filmId))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<EfFilm?> GetFilmById_EntityFramework_NoTracking_WithSessionSettings()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_EntityFramework_NoTracking_WithSessionSettings);
        var sql = BuildFilmByIdSql(param => $"@{param}");
        return await _efDbContextWithSettings.Films
            .FromSqlRaw(sql, new NpgsqlParameter("id", _filmId))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<EfFilmActor?> GetFilmActorComposite_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(GetFilmActorComposite_EntityFramework);
        var sql = BuildFilmActorCompositeSql(param => $"@{param}");
        return await _efDbContext.FilmActors
            .FromSqlRaw(
                sql,
                new NpgsqlParameter("actorId", _compositeKey.actorId),
                new NpgsqlParameter("filmId", _compositeKey.filmId))
            .FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<EfFilmActor?> GetFilmActorComposite_EntityFramework_WithSessionSettings()
    {
        _currentBenchmarkLabel = nameof(GetFilmActorComposite_EntityFramework_WithSessionSettings);
        var sql = BuildFilmActorCompositeSql(param => $"@{param}");
        return await _efDbContextWithSettings.FilmActors
            .FromSqlRaw(
                sql,
                new NpgsqlParameter("actorId", _compositeKey.actorId),
                new NpgsqlParameter("filmId", _compositeKey.filmId))
            .FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<EfFilmActor?> GetFilmActorComposite_EntityFramework_NoTracking()
    {
        _currentBenchmarkLabel = nameof(GetFilmActorComposite_EntityFramework_NoTracking);
        var sql = BuildFilmActorCompositeSql(param => $"@{param}");
        return await _efDbContext.FilmActors
            .FromSqlRaw(
                sql,
                new NpgsqlParameter("actorId", _compositeKey.actorId),
                new NpgsqlParameter("filmId", _compositeKey.filmId))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<EfFilmActor?> GetFilmActorComposite_EntityFramework_NoTracking_WithSessionSettings()
    {
        _currentBenchmarkLabel = nameof(GetFilmActorComposite_EntityFramework_NoTracking_WithSessionSettings);
        var sql = BuildFilmActorCompositeSql(param => $"@{param}");
        return await _efDbContextWithSettings.FilmActors
            .FromSqlRaw(
                sql,
                new NpgsqlParameter("actorId", _compositeKey.actorId),
                new NpgsqlParameter("filmId", _compositeKey.filmId))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<int> UpdateFilm_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(UpdateFilm_EntityFramework);
        var lengthSql = BuildFilmLengthByIdSql(param => $"@{param}");
        var currentLength = await _efDbContext.FilmLengthRows
            .FromSqlRaw(lengthSql, new NpgsqlParameter("id", _filmId))
            .AsNoTracking()
            .Select(r => r.Length)
            .FirstAsync();

        var newLength = _flip ? currentLength + 1 : currentLength - 1;
        _flip = !_flip;

        var updateSql = BuildFilmUpdateLengthSql(param => $"@{param}");
        return await _efDbContext.Database.ExecuteSqlRawAsync(
            updateSql,
            new NpgsqlParameter("id", _filmId),
            new NpgsqlParameter("len", newLength));
    }

    [Benchmark]
    public async Task<int> UpdateFilm_EntityFramework_WithSessionSettings()
    {
        _currentBenchmarkLabel = nameof(UpdateFilm_EntityFramework_WithSessionSettings);
        var lengthSql = BuildFilmLengthByIdSql(param => $"@{param}");
        var currentLength = await _efDbContextWithSettings.FilmLengthRows
            .FromSqlRaw(lengthSql, new NpgsqlParameter("id", _filmId))
            .AsNoTracking()
            .Select(r => r.Length)
            .FirstAsync();

        var newLength = _flip ? currentLength + 1 : currentLength - 1;
        _flip = !_flip;

        var updateSql = BuildFilmUpdateLengthSql(param => $"@{param}");
        return await _efDbContextWithSettings.Database.ExecuteSqlRawAsync(
            updateSql,
            new NpgsqlParameter("id", _filmId),
            new NpgsqlParameter("len", newLength));
    }

    [Benchmark]
    public async Task<int> InsertThenDeleteFilm_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(InsertThenDeleteFilm_EntityFramework);
        var title = $"Bench_{Interlocked.Increment(ref _runCounter):D10}";
        var insertSql = BuildFilmInsertSql(param => $"@{param}");
        var id = await ExecuteScalarAsync(
            _efDbContext,
            insertSql,
            new NpgsqlParameter("title", title),
            new NpgsqlParameter("length", 123));
        var deleteSql = BuildFilmDeleteSql(param => $"@{param}");
        return await ExecuteNonQueryAsync(
            _efDbContext,
            deleteSql,
            new NpgsqlParameter("id", id));
    }

    [Benchmark]
    public async Task<int> InsertThenDeleteFilm_EntityFramework_WithSessionSettings()
    {
        _currentBenchmarkLabel = nameof(InsertThenDeleteFilm_EntityFramework_WithSessionSettings);
        var title = $"Bench_{Interlocked.Increment(ref _runCounter):D10}";
        var insertSql = BuildFilmInsertSql(param => $"@{param}");
        var id = await ExecuteScalarAsync(
            _efDbContextWithSettings,
            insertSql,
            new NpgsqlParameter("title", title),
            new NpgsqlParameter("length", 123));
        var deleteSql = BuildFilmDeleteSql(param => $"@{param}");
        return await ExecuteNonQueryAsync(
            _efDbContextWithSettings,
            deleteSql,
            new NpgsqlParameter("id", id));
    }

    [Benchmark]
    public async Task<List<EfFilm>> GetTenFilms_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(GetTenFilms_EntityFramework);
        var sql = BuildFilmIdsSql(param => $"@{param}");
        var idsParam = new NpgsqlParameter<int[]>("ids", _filmIds10.ToArray())
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer
        };
        return await _efDbContext.Films
            .FromSqlRaw(sql, idsParam)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<EfFilm>> GetTenFilms_EntityFramework_WithSessionSettings()
    {
        _currentBenchmarkLabel = nameof(GetTenFilms_EntityFramework_WithSessionSettings);
        var sql = BuildFilmIdsSql(param => $"@{param}");
        var idsParam = new NpgsqlParameter<int[]>("ids", _filmIds10.ToArray())
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer
        };
        return await _efDbContextWithSettings.Films
            .FromSqlRaw(sql, idsParam)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<EfFilm>> GetTenFilms_EntityFramework_NoTracking()
    {
        _currentBenchmarkLabel = nameof(GetTenFilms_EntityFramework_NoTracking);
        var sql = BuildFilmIdsSql(param => $"@{param}");
        var idsParam = new NpgsqlParameter<int[]>("ids", _filmIds10.ToArray())
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer
        };
        return await _efDbContext.Films
            .FromSqlRaw(sql, idsParam)
            .AsNoTracking()
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<EfFilm>> GetTenFilms_EntityFramework_NoTracking_WithSessionSettings()
    {
        _currentBenchmarkLabel = nameof(GetTenFilms_EntityFramework_NoTracking_WithSessionSettings);
        var sql = BuildFilmIdsSql(param => $"@{param}");
        var idsParam = new NpgsqlParameter<int[]>("ids", _filmIds10.ToArray())
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer
        };
        return await _efDbContextWithSettings.Films
            .FromSqlRaw(sql, idsParam)
            .AsNoTracking()
            .ToListAsync();
    }

    [Benchmark]
    public async Task GetFilmById_Mine_Concurrent()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_Mine_Concurrent);
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var container = _ctx.CreateSqlContainer();
            var sql = BuildFilmByIdSql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("id", DbType.Int32, _filmId);
            await using var reader = await container.ExecuteReaderSingleRowAsync();
            if (await reader.ReadAsync())
            {
                _filmHelper.MapReaderToObject(reader);
            }
        });
    }

    [Benchmark]
    public async Task GetFilmById_Dapper_Concurrent()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_Dapper_Concurrent);
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            var sql = BuildFilmByIdSql(param => $"@{param}");
            var row = await conn.QuerySingleOrDefaultAsync<DapperFilmRow>(sql, new { id = _filmId });
            if (row != null)
            {
                MapFilm(row);
            }
        });
    }

    [Benchmark]
    public async Task GetFilmById_Dapper_WithSessionSettings_Concurrent()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_Dapper_WithSessionSettings_Concurrent);
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = await _dapperSettingsDataSource.OpenConnectionAsync();
            await BenchmarkSessionSettings.ApplyAsync(conn, BenchmarkSessionSettings.PostgresSessionSettings);
            var sql = BuildFilmByIdSql(param => $"@{param}");
            var row = await conn.QuerySingleOrDefaultAsync<DapperFilmRow>(sql, new { id = _filmId });
            if (row != null)
            {
                MapFilm(row);
            }
        });
    }

    [Benchmark]
    public async Task GetFilmById_EntityFramework_NoTracking_Concurrent()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_EntityFramework_NoTracking_Concurrent);
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new PagilaDbContext(_efOptions);
            var sql = BuildFilmByIdSql(param => $"@{param}");
            await ctx.Films
                .FromSqlRaw(sql, new NpgsqlParameter("id", _filmId))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        });
    }

    [Benchmark]
    public async Task GetFilmById_EntityFramework_NoTracking_WithSessionSettings_Concurrent()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_EntityFramework_NoTracking_WithSessionSettings_Concurrent);
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new PagilaDbContext(_efOptionsWithSettings);
            var sql = BuildFilmByIdSql(param => $"@{param}");
            await ctx.Films
                .FromSqlRaw(sql, new NpgsqlParameter("id", _filmId))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        });
    }

    private static string BuildFilmByIdSql(Func<string, string> param)
    {
        return FilmByIdSqlTemplate.Replace("{id}", param("id"));
    }

    private static string BuildFilmActorCompositeSql(Func<string, string> param)
    {
        return FilmActorCompositeSqlTemplate
            .Replace("{actorId}", param("actorId"))
            .Replace("{filmId}", param("filmId"));
    }

    private static string BuildFilmLengthByIdSql(Func<string, string> param)
    {
        return FilmLengthByIdSqlTemplate.Replace("{id}", param("id"));
    }

    private static string BuildFilmUpdateLengthSql(Func<string, string> param)
    {
        return FilmUpdateLengthSqlTemplate
            .Replace("{id}", param("id"))
            .Replace("{len}", param("len"));
    }

    private static string BuildFilmInsertSql(Func<string, string> param)
    {
        return FilmInsertSqlTemplate
            .Replace("{title}", param("title"))
            .Replace("{length}", param("length"));
    }

    private static string BuildFilmDeleteSql(Func<string, string> param)
    {
        return FilmDeleteSqlTemplate.Replace("{id}", param("id"));
    }

    private static string BuildFilmIdsSql(Func<string, string> param)
    {
        return FilmIdsSqlTemplate.Replace("{ids}", param("ids"));
    }

    private static Film MapFilm(DapperFilmRow row)
    {
        return new Film
        {
            Id = row.film_id,
            Title = row.title,
            Length = row.length
        };
    }

    private static FilmActor MapFilmActor(DapperFilmActorRow row)
    {
        return new FilmActor
        {
            ActorId = row.actor_id,
            FilmId = row.film_id
        };
    }

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

    // Entities
    [Table("film", "public")]
    public class Film
    {
        [Id(false)]
        [Column("film_id", DbType.Int32)]
        public int Id { get; set; }

        [Column("title", DbType.String)] public string Title { get; set; } = string.Empty;

        [Column("length", DbType.Int32)] public int Length { get; set; }
    }

    [Table("film_actor", "public")]
    public class FilmActor
    {
        [pengdows.crud.attributes.PrimaryKey(1)]
        [Column("actor_id", DbType.Int32)]
        public int ActorId { get; set; }

        [pengdows.crud.attributes.PrimaryKey(2)]
        [Column("film_id", DbType.Int32)]
        public int FilmId { get; set; }
    }

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

    // Entity Framework DbContext and entities
    public class PagilaDbContext : DbContext
    {
        public PagilaDbContext(DbContextOptions<PagilaDbContext> options) : base(options)
        {
        }

        public DbSet<EfFilm> Films { get; set; }
        public DbSet<EfActor> Actors { get; set; }
        public DbSet<EfFilmActor> FilmActors { get; set; }
        public DbSet<FilmLengthRow> FilmLengthRows { get; set; }

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

            modelBuilder.Entity<EfActor>(entity =>
            {
                entity.ToTable("actor");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("actor_id").ValueGeneratedOnAdd();
                entity.Property(e => e.FirstName).HasColumnName("first_name").IsRequired();
                entity.Property(e => e.LastName).HasColumnName("last_name").IsRequired();
            });

            modelBuilder.Entity<EfFilmActor>(entity =>
            {
                entity.ToTable("film_actor");
                entity.HasKey(e => new { e.ActorId, e.FilmId });
                entity.Property(e => e.ActorId).HasColumnName("actor_id");
                entity.Property(e => e.FilmId).HasColumnName("film_id");

                entity.HasOne(fa => fa.Actor)
                    .WithMany(a => a.FilmActors)
                    .HasForeignKey(fa => fa.ActorId);

                entity.HasOne(fa => fa.Film)
                    .WithMany(f => f.FilmActors)
                    .HasForeignKey(fa => fa.FilmId);
            });

            modelBuilder.Entity<FilmLengthRow>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
                entity.Property(e => e.Length).HasColumnName("length");
            });
        }
    }

    // Entity Framework entity classes
    public class EfFilm
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Length { get; set; }
        public virtual ICollection<EfFilmActor> FilmActors { get; set; } = new List<EfFilmActor>();
    }

    public class EfActor
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public virtual ICollection<EfFilmActor> FilmActors { get; set; } = new List<EfFilmActor>();
    }

    public class EfFilmActor
    {
        public int ActorId { get; set; }
        public int FilmId { get; set; }
        public virtual EfActor Actor { get; set; } = null!;
        public virtual EfFilm Film { get; set; } = null!;
    }

    [Keyless]
    public class FilmLengthRow
    {
        public int Length { get; set; }
    }
}
