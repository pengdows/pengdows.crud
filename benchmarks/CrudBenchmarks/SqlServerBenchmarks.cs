using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// SQL Server performance comparison between pengdows.crud, Dapper, and EF Core
/// Tests SQL Server native @parameter performance vs PostgreSQL
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 10, invocationCount: 100)]
public class SqlServerBenchmarks : IAsyncDisposable
{
    private const string FilmByIdSqlTemplate = """
        SELECT film_id, title, length
        FROM film
        WHERE film_id = {id}
        """;

    private const string FilmActorCompositeSqlTemplate = """
        SELECT actor_id, film_id
        FROM film_actor
        WHERE actor_id = {actorId} AND film_id = {filmId}
        """;
    private IContainer? _container;
    private string _baseConnStr = string.Empty;
    private string _pengdowsConnStr = string.Empty;
    private string _efConnStr = string.Empty;
    private string _efSettingsConnStr = string.Empty;
    private string _dapperConnStr = string.Empty;
    private string _dapperSettingsConnStr = string.Empty;
    private IDatabaseContext _ctx = null!;
    private TypeMapRegistry _map = null!;
    private TableGateway<Film, int> _filmHelper = null!;
    private TableGateway<FilmActor, int> _filmActorHelper = null!;
    private SqlServerDbContext _efDbContext = null!;
    private SqlServerDbContext _efDbContextWithSettings = null!;
    private DbContextOptions<SqlServerDbContext> _efOptions = null!;
    private DbContextOptions<SqlServerDbContext> _efOptionsWithSettings = null!;
    private SqlConnection _dapperConnection = null!;
    private SqlConnection _dapperConnectionWithSessionSettings = null!;

    [Params(1000)] public int FilmCount;

    [Params(200)] public int ActorCount;
    [Params(16)] public int Parallelism;
    [Params(64)] public int OperationsPerRun;

    private int _filmId;
    private List<int> _filmIds10 = new();
    private (int actorId, int filmId) _compositeKey;

    // Cached containers for cloning tests
    private ISqlContainer _cachedFilmRetrieveContainer = null!;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithEnvironment("MSSQL_SA_PASSWORD", "YourPassword123!")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithPortBinding(1433, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
            .Build();

        await _container.StartAsync();

        var mappedPort = _container.GetMappedPublicPort(1433);
        var masterConnStr =
            $"Server=localhost,{mappedPort};Database=master;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true;Connection Timeout=60;";

        await WaitForReady(masterConnStr);
        await CreateDatabaseAsync(masterConnStr);

        _baseConnStr =
            $"Server=localhost,{mappedPort};Database=testdb;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true;Connection Timeout=30;Connection Lifetime=0;";
        _pengdowsConnStr = _baseConnStr + "Application Name=Benchmark_PengdowsCrud;";
        _efConnStr = _baseConnStr + "Application Name=Benchmark_EntityFramework;";
        _efSettingsConnStr = _baseConnStr + "Application Name=Benchmark_EntityFramework_Settings;";
        _dapperConnStr = _baseConnStr + "Application Name=Benchmark_Dapper;";
        _dapperSettingsConnStr = _baseConnStr + "Application Name=Benchmark_Dapper_Settings;";
        await CreateSchemaAndSeedAsync();

        _map = new TypeMapRegistry();
        _map.Register<Film>();
        _map.Register<FilmActor>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _pengdowsConnStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _ctx = new DatabaseContext(cfg, SqlClientFactory.Instance, null, _map);

        Console.WriteLine($"[BENCHMARK] Configured DbMode: {cfg.DbMode}");
        Console.WriteLine($"[BENCHMARK] Actual ConnectionMode: {_ctx.ConnectionMode}");

        _filmHelper = new TableGateway<Film, int>(_ctx);
        _filmActorHelper = new TableGateway<FilmActor, int>(_ctx);

        // Initialize Entity Framework DbContext
        _efOptions = new DbContextOptionsBuilder<SqlServerDbContext>()
            .UseSqlServer(_efConnStr)
            .Options;
        _efDbContext = new SqlServerDbContext(_efOptions);

        // Initialize Entity Framework DbContext with session settings parity
        _efOptionsWithSettings = new DbContextOptionsBuilder<SqlServerDbContext>()
            .UseSqlServer(_efSettingsConnStr)
            .AddInterceptors(new SessionSettingsConnectionInterceptor(BenchmarkSessionSettings.SqlServerSessionSettings))
            .Options;
        _efDbContextWithSettings = new SqlServerDbContext(_efOptionsWithSettings);

        // Initialize Dapper connection
        _dapperConnection = new SqlConnection(_dapperConnStr);
        await _dapperConnection.OpenAsync();

        _dapperConnectionWithSessionSettings = new SqlConnection(_dapperSettingsConnStr);
        await _dapperConnectionWithSessionSettings.OpenAsync();
        await BenchmarkSessionSettings.ApplyAsync(
            _dapperConnectionWithSessionSettings,
            BenchmarkSessionSettings.SqlServerSessionSettings);

        // Pick keys to use in benchmarks
        _filmId = await _dapperConnection.ExecuteScalarAsync<int>("SELECT TOP 1 film_id FROM film ORDER BY film_id");
        var row = await _dapperConnection.QuerySingleAsync<(int actor_id, int film_id)>(
            "SELECT TOP 1 actor_id, film_id FROM film_actor");
        _compositeKey = (row.actor_id, row.film_id);
        _filmIds10 = (await _dapperConnection.QueryAsync<int>("SELECT TOP 10 film_id FROM film ORDER BY film_id"))
            .ToList();

        // Warmup systems
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
        var dapperWarmupSql = BuildFilmByIdSql(param => $"@{param}");
        var dapperWarmupRow = await _dapperConnection.QuerySingleOrDefaultAsync<DapperFilmRow>(
            dapperWarmupSql,
            new { id = _filmId });
        var dapperWarmup = dapperWarmupRow == null ? null : MapFilm(dapperWarmupRow);
        Console.WriteLine($"[WARMUP] Dapper warmed up - retrieved film: {dapperWarmup?.Title}");

        Console.WriteLine("[WARMUP] Warming up Entity Framework...");
        var efWarmupSql = BuildFilmByIdSql(param => $"@{param}");
        var efWarmup = await _efDbContext.Films
            .FromSqlRaw(efWarmupSql, new SqlParameter("id", _filmId))
            .FirstOrDefaultAsync();
        Console.WriteLine($"[WARMUP] Entity Framework warmed up - retrieved film: {efWarmup?.Title}");

        // Pre-build cached container for cloning benchmark
        Console.WriteLine("[WARMUP] Pre-building cached container for cloning...");
        _cachedFilmRetrieveContainer = _ctx.CreateSqlContainer();
        _cachedFilmRetrieveContainer.Query.Append(
            BuildFilmByIdSql(param => _cachedFilmRetrieveContainer.MakeParameterName(param)));
        _cachedFilmRetrieveContainer.AddParameterWithValue("id", DbType.Int32, _filmId);
        Console.WriteLine("[WARMUP] Cached container ready for cloning benchmark");
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        // Capture SQL Server execution statistics before cleanup
        await PrintSqlServerStatistics();

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

        if (_dapperConnection != null)
        {
            await _dapperConnection.DisposeAsync();
        }

        if (_dapperConnectionWithSessionSettings != null)
        {
            await _dapperConnectionWithSessionSettings.DisposeAsync();
        }

        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    private async Task PrintSqlServerStatistics()
    {
        try
        {
            await using var conn = new SqlConnection(_baseConnStr);
            await conn.OpenAsync();

            // Query SQL Server execution statistics
            var statsQuery = @"
                SELECT
                    qs.sql_handle,
                    qs.execution_count,
                    qs.total_elapsed_time / 1000.0 as total_elapsed_time_ms,
                    qs.total_elapsed_time / qs.execution_count / 1000.0 as avg_elapsed_time_ms,
                    qs.total_worker_time / 1000.0 as total_cpu_time_ms,
                    qs.total_worker_time / qs.execution_count / 1000.0 as avg_cpu_time_ms,
                    qs.total_logical_reads,
                    qs.total_logical_reads / qs.execution_count as avg_logical_reads,
                    SUBSTRING(st.text, (qs.statement_start_offset/2)+1,
                        ((CASE qs.statement_end_offset
                            WHEN -1 THEN DATALENGTH(st.text)
                            ELSE qs.statement_end_offset
                        END - qs.statement_start_offset)/2) + 1) as statement_text
                FROM sys.dm_exec_query_stats qs
                CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
                WHERE st.text LIKE '%film%'
                  AND st.text NOT LIKE '%sys.dm_exec%'
                  AND qs.execution_count > 10
                ORDER BY qs.execution_count DESC";

            var results = await conn.QueryAsync(statsQuery);

            Console.WriteLine();
            Console.WriteLine("==== SQL Server Execution Statistics ====");
            foreach (var row in results.Take(5)) // Top 5 most executed queries
            {
                Console.WriteLine($"Executions: {row.execution_count}");
                Console.WriteLine($"Avg Elapsed Time: {row.avg_elapsed_time_ms:F3} ms");
                Console.WriteLine($"Avg CPU Time: {row.avg_cpu_time_ms:F3} ms");
                Console.WriteLine($"Avg Logical Reads: {row.avg_logical_reads}");
                Console.WriteLine($"SQL: {row.statement_text}");
                Console.WriteLine("---");
            }

            Console.WriteLine("============================================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to collect SQL Server statistics: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
    }

    private async Task WaitForReady(string connectionString)
    {
        for (var i = 0; i < 120; i++) // Extended timeout for SQL Server
        {
            try
            {
                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
                Console.WriteLine($"[SQL Server] Connection successful on attempt {i + 1}");
                return;
            }
            catch (Exception ex)
            {
                if (i % 10 == 0) // Log every 10 attempts
                {
                    Console.WriteLine($"[SQL Server] Connection attempt {i + 1}/120 failed: {ex.Message}");
                }

                await Task.Delay(2000); // Longer delay for SQL Server startup
            }
        }

        throw new TimeoutException("SQL Server container did not become ready after 240 seconds");
    }

    private async Task CreateDatabaseAsync(string masterConnectionString)
    {
        await using var conn = new SqlConnection(masterConnectionString);
        await conn.OpenAsync();

        var checkDbCommand = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = 'testdb'", conn);
        var result = await checkDbCommand.ExecuteScalarAsync();
        var dbExists = Convert.ToInt32(result ?? 0) > 0;

        if (!dbExists)
        {
            var createDbCommand = new SqlCommand("CREATE DATABASE testdb", conn);
            await createDbCommand.ExecuteNonQueryAsync();
            Console.WriteLine("[SQL Server] Database 'testdb' created successfully");
        }
        else
        {
            Console.WriteLine("[SQL Server] Database 'testdb' already exists");
        }
    }

    private async Task CreateSchemaAndSeedAsync()
    {
        await using var conn = new SqlConnection(_baseConnStr);
        await conn.OpenAsync();

        // Create tables
        var createTablesSql = @"
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[film]') AND type in (N'U'))
CREATE TABLE [dbo].[film] (
    [film_id] [int] IDENTITY(1,1) NOT NULL,
    [title] [nvarchar](255) NOT NULL,
    [length] [int] NOT NULL,
    CONSTRAINT [PK_film] PRIMARY KEY CLUSTERED ([film_id] ASC)
);

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[film_actor]') AND type in (N'U'))
CREATE TABLE [dbo].[film_actor] (
    [actor_id] [int] NOT NULL,
    [film_id] [int] NOT NULL,
    CONSTRAINT [PK_film_actor] PRIMARY KEY CLUSTERED ([actor_id] ASC, [film_id] ASC)
);";
        const int commandTimeoutSeconds = 120;

        await conn.ExecuteAsync(createTablesSql, commandTimeout: commandTimeoutSeconds);

        // Clear existing data and reset identity values so film IDs remain predictable
        await conn.ExecuteAsync("TRUNCATE TABLE [film_actor]; TRUNCATE TABLE [film];",
            commandTimeout: commandTimeoutSeconds);

        // Use bulk copy to seed films quickly
        var filmTable = new DataTable();
        filmTable.Columns.Add("title", typeof(string));
        filmTable.Columns.Add("length", typeof(int));
        for (var i = 0; i < FilmCount; i++)
        {
            filmTable.Rows.Add($"Film {i}", 60 + i % 120);
        }

        await using var transaction = await conn.BeginTransactionAsync();

        using (var filmBulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.TableLock, (SqlTransaction)transaction)
               {
                   DestinationTableName = "[dbo].[film]",
                   BatchSize = 500,
                   BulkCopyTimeout = commandTimeoutSeconds
               })
        {
            filmBulkCopy.ColumnMappings.Add("title", "title");
            filmBulkCopy.ColumnMappings.Add("length", "length");
            await filmBulkCopy.WriteToServerAsync(filmTable);
        }

        // Build associations once we know film IDs will be 1..FilmCount
        var filmActorTable = new DataTable();
        filmActorTable.Columns.Add("actor_id", typeof(int));
        filmActorTable.Columns.Add("film_id", typeof(int));
        var filmStep = Math.Max(1, FilmCount / 50);
        for (var actorId = 1; actorId <= ActorCount; actorId++)
        {
            for (var filmId = actorId; filmId <= FilmCount; filmId += filmStep)
            {
                filmActorTable.Rows.Add(actorId, filmId);
            }
        }

        using (var filmActorBulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.TableLock, (SqlTransaction)transaction)
               {
                   DestinationTableName = "[dbo].[film_actor]",
                   BatchSize = 1000,
                   BulkCopyTimeout = commandTimeoutSeconds
               })
        {
            filmActorBulkCopy.ColumnMappings.Add("actor_id", "actor_id");
            filmActorBulkCopy.ColumnMappings.Add("film_id", "film_id");
            await filmActorBulkCopy.WriteToServerAsync(filmActorTable);
        }

        await transaction.CommitAsync();

        Console.WriteLine(
            $"[SQL Server] Seeded {FilmCount} films and {filmActorTable.Rows.Count} film_actor associations");
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_Mine()
    {
        await using var container = _ctx.CreateSqlContainer();
        var sql = BuildFilmByIdSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("id", DbType.Int32, _filmId);

        await using var reader = await container.ExecuteReaderSingleRowAsync();
        return await reader.ReadAsync() ? _filmHelper.MapReaderToObject(reader) : null;
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_Mine_WithCloning()
    {
        var clone = _cachedFilmRetrieveContainer.Clone();
        clone.SetParameterValue("id", _filmId);
        await using var reader = await clone.ExecuteReaderSingleRowAsync();
        return await reader.ReadAsync() ? _filmHelper.MapReaderToObject(reader) : null;
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_Dapper()
    {
        var sql = BuildFilmByIdSql(param => $"@{param}");
        var row = await _dapperConnection.QuerySingleOrDefaultAsync<DapperFilmRow>(sql, new { id = _filmId });
        return row == null ? null : MapFilm(row);
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_Dapper_WithSessionSettings()
    {
        var sql = BuildFilmByIdSql(param => $"@{param}");
        var row = await _dapperConnectionWithSessionSettings.QuerySingleOrDefaultAsync<DapperFilmRow>(
            sql,
            new { id = _filmId });
        return row == null ? null : MapFilm(row);
    }

    [Benchmark]
    public async Task<FilmActor?> GetFilmActorComposite_Mine()
    {
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
        var sql = BuildFilmActorCompositeSql(param => $"@{param}");
        var row = await _dapperConnection.QuerySingleOrDefaultAsync<DapperFilmActorRow>(
            sql,
            new { actorId = _compositeKey.actorId, filmId = _compositeKey.filmId });
        return row == null ? null : MapFilmActor(row);
    }

    [Benchmark]
    public async Task<FilmActor?> GetFilmActorComposite_Dapper_WithSessionSettings()
    {
        var sql = BuildFilmActorCompositeSql(param => $"@{param}");
        var row = await _dapperConnectionWithSessionSettings.QuerySingleOrDefaultAsync<DapperFilmActorRow>(
            sql,
            new { actorId = _compositeKey.actorId, filmId = _compositeKey.filmId });
        return row == null ? null : MapFilmActor(row);
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_EntityFramework()
    {
        var sql = BuildFilmByIdSql(param => $"@{param}");
        return await _efDbContext.Films
            .FromSqlRaw(sql, new SqlParameter("id", _filmId))
            .FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_EntityFramework_WithSessionSettings()
    {
        var sql = BuildFilmByIdSql(param => $"@{param}");
        return await _efDbContextWithSettings.Films
            .FromSqlRaw(sql, new SqlParameter("id", _filmId))
            .FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_EntityFramework_NoTracking()
    {
        var sql = BuildFilmByIdSql(param => $"@{param}");
        return await _efDbContext.Films
            .FromSqlRaw(sql, new SqlParameter("id", _filmId))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_EntityFramework_NoTracking_WithSessionSettings()
    {
        var sql = BuildFilmByIdSql(param => $"@{param}");
        return await _efDbContextWithSettings.Films
            .FromSqlRaw(sql, new SqlParameter("id", _filmId))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task GetFilmById_Mine_Concurrent()
    {
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
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = new SqlConnection(_dapperConnStr);
            await conn.OpenAsync();
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
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = new SqlConnection(_dapperSettingsConnStr);
            await conn.OpenAsync();
            await BenchmarkSessionSettings.ApplyAsync(
                conn,
                BenchmarkSessionSettings.SqlServerSessionSettings);
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
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new SqlServerDbContext(_efOptions);
            var sql = BuildFilmByIdSql(param => $"@{param}");
            await ctx.Films
                .FromSqlRaw(sql, new SqlParameter("id", _filmId))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        });
    }

    [Benchmark]
    public async Task GetFilmById_EntityFramework_NoTracking_WithSessionSettings_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new SqlServerDbContext(_efOptionsWithSettings);
            var sql = BuildFilmByIdSql(param => $"@{param}");
            await ctx.Films
                .FromSqlRaw(sql, new SqlParameter("id", _filmId))
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

    [Table("film")]
    public class Film
    {
        [Id(false)]
        [Column("film_id", DbType.Int32)]
        public int Id { get; set; }

        [Column("title", DbType.String)] public string Title { get; set; } = string.Empty;

        [Column("length", DbType.Int32)] public int Length { get; set; }
    }

    [Table("film_actor")]
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

    public class SqlServerDbContext : DbContext
    {
        public SqlServerDbContext(DbContextOptions<SqlServerDbContext> options) : base(options)
        {
        }

        public DbSet<Film> Films { get; set; } = null!;
        public DbSet<FilmActor> FilmActors { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Film>(entity =>
            {
                entity.ToTable("film");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("film_id");
                entity.Property(e => e.Title).HasColumnName("title");
                entity.Property(e => e.Length).HasColumnName("length");
            });

            modelBuilder.Entity<FilmActor>(entity =>
            {
                entity.ToTable("film_actor");
                entity.HasKey(e => new { e.ActorId, e.FilmId });
                entity.Property(e => e.ActorId).HasColumnName("actor_id");
                entity.Property(e => e.FilmId).HasColumnName("film_id");
            });
        }
    }
}
