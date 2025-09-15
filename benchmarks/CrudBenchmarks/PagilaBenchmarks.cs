using System.Data;
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
    private IContainer? _container;
    private string _connStr = string.Empty;
    private IDatabaseContext _ctx = null!;
    private TypeMapRegistry _map = null!;
    private EntityHelper<Film, int> _filmHelper = null!;
    private EntityHelper<FilmActor, int> _filmActorHelper = null!;
    private PagilaDbContext _efDbContext = null!;
    private NpgsqlDataSource _dapperDataSource = null!;
    // Remove manual template caching - use EntityHelper's built-in caching

    [Params(1000)]
    public int FilmCount;

    [Params(200)]
    public int ActorCount;

    private int _filmId;
    private List<int> _filmIds10 = new();
    private bool _flip;
    private (int actorId, int filmId) _compositeKey;
    private long _runCounter;
    [ThreadStatic] private static string? _currentBenchmarkLabel;
    private bool _collectPerIteration = false; // Disable for representative benchmarks

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
        _connStr = $"Host=localhost;Port={mappedPort};Database=pagila;Username=postgres;Password=postgres;Maximum Pool Size=100";

        await WaitForReady();
        await CreateSchemaAndSeedAsync();

        // Initialize pg_stat_statements and reset stats before running benchmarks
        await using (var admin = new NpgsqlConnection(_connStr))
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
            ConnectionString = _connStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _ctx = new DatabaseContext(cfg, NpgsqlFactory.Instance, null, _map);

        // Verify the actual mode being used
        Console.WriteLine($"[BENCHMARK] Configured DbMode: {cfg.DbMode}");
        Console.WriteLine($"[BENCHMARK] Actual ConnectionMode: {_ctx.ConnectionMode}");

        _filmHelper = new EntityHelper<Film, int>(_ctx);
        _filmActorHelper = new EntityHelper<FilmActor, int>(_ctx);

        // Initialize Entity Framework DbContext
        var options = new DbContextOptionsBuilder<PagilaDbContext>()
            .UseNpgsql(_connStr)
            .Options;
        _efDbContext = new PagilaDbContext(options);

        // Initialize Dapper data source for fair comparison
        _dapperDataSource = NpgsqlDataSource.Create(_connStr);

        // EntityHelper will handle internal caching and cloning automatically

        // pick keys to use in benchmarks
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        _filmId = await conn.ExecuteScalarAsync<int>("select film_id from film order by film_id limit 1");
        var row = await conn.QuerySingleAsync<(int actor_id, int film_id)>("select actor_id, film_id from film_actor limit 1");
        _compositeKey = (row.actor_id, row.film_id);
        _filmIds10 = (await conn.QueryAsync<int>("select film_id from film order by film_id limit 10")).ToList();

        // Warmup both systems to ensure fair comparison
        Console.WriteLine("[WARMUP] Warming up pengdows.crud...");
        var warmupFilm = await _filmHelper.RetrieveOneAsync(_filmId);
        Console.WriteLine($"[WARMUP] pengdows.crud warmed up - retrieved film: {warmupFilm?.Title}");

        Console.WriteLine("[WARMUP] Warming up Dapper...");
        await using var warmupConn = await _dapperDataSource.OpenConnectionAsync();
        var dapperWarmup = await warmupConn.QuerySingleOrDefaultAsync<Film>(
            "select film_id as \"Id\", title as \"Title\", length as \"Length\" from film where film_id=@id",
            new { id = _filmId });
        Console.WriteLine($"[WARMUP] Dapper warmed up - retrieved film: {dapperWarmup?.Title}");

        Console.WriteLine("[WARMUP] Warming up Entity Framework...");
        var efWarmup = await _efDbContext.Films.FirstOrDefaultAsync(f => f.Id == _filmId);
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

        if (_dapperDataSource != null)
        {
            await _dapperDataSource.DisposeAsync();
        }

        // Dump Postgres statistics for analysis
        try
        {
            await PgStats.DumpSummaryAsync(_connStr);
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
            PgStats.ResetAsync(_connStr).GetAwaiter().GetResult();
        }
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        if (_collectPerIteration)
        {
            var label = _currentBenchmarkLabel ?? "(unknown)";
            PgStats.DumpSummaryAsync(_connStr, label).GetAwaiter().GetResult();
        }
    }

    private async Task WaitForReady()
    {
        // Simple retry loop until the DB accepts connections
        for (int i = 0; i < 60; i++)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connStr);
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
        await using var conn = new NpgsqlConnection(_connStr);
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
            for (int i = 0; i < ActorCount; i++)
            {
                await conn.ExecuteAsync(ins, new { f = $"A{i}", l = $"L{i}" }, tx);
            }
        }

        // Seed films
        {
            const string ins = "insert into film(title, length) values (@t, @len)";
            for (int i = 0; i < FilmCount; i++)
            {
                await conn.ExecuteAsync(ins, new { t = $"Film {i}", len = 60 + (i % 120) }, tx);
            }
        }

        // Seed film_actor associations (simple round-robin)
        {
            const string ins = "insert into film_actor(actor_id, film_id) values (@a, @f)";
            for (int i = 1; i <= ActorCount; i++)
            {
                for (int f = i; f <= FilmCount; f += Math.Max(1, FilmCount / 50)) // ~50 films per actor
                {
                    await conn.ExecuteAsync(ins, new { a = i, f }, tx);
                }
            }
        }

        await tx.CommitAsync();
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_Mine()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_Mine);
        return await _filmHelper.RetrieveOneAsync(_filmId, _ctx);
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_Dapper()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_Dapper);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<Film>(
            "select film_id as \"Id\", title as \"Title\", length as \"Length\" from film where film_id=@id",
            new { id = _filmId });
    }

    [Benchmark]
    public async Task<FilmActor?> GetFilmActorComposite_Mine()
    {
        _currentBenchmarkLabel = nameof(GetFilmActorComposite_Mine);
        // Use simple approach for composite key retrieval
        var key = new FilmActor { ActorId = _compositeKey.actorId, FilmId = _compositeKey.filmId };
        return await _filmActorHelper.RetrieveOneAsync(key);
    }

    [Benchmark]
    public async Task<FilmActor?> GetFilmActorComposite_Dapper()
    {
        _currentBenchmarkLabel = nameof(GetFilmActorComposite_Dapper);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<FilmActor>(
            "select actor_id as \"ActorId\", film_id as \"FilmId\" from film_actor where actor_id=@a and film_id=@f",
            new { a = _compositeKey.actorId, f = _compositeKey.filmId });
    }

    [Benchmark]
    public async Task<int> UpdateFilm_Mine()
    {
        _currentBenchmarkLabel = nameof(UpdateFilm_Mine);

        // Retrieve film
        var film = await _filmHelper.RetrieveOneAsync(_filmId);
        if (film == null) return 0;

        // Toggle length to avoid no-op updates
        film.Length = _flip ? film.Length + 1 : film.Length - 1;
        _flip = !_flip;

        // Update film
        return await _filmHelper.UpdateAsync(film);
    }

    [Benchmark]
    public async Task<int> UpdateFilm_Dapper()
    {
        _currentBenchmarkLabel = nameof(UpdateFilm_Dapper);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var len = await conn.ExecuteScalarAsync<int>(
            "select length from film where film_id=@id", new { id = _filmId });
        var newLen = _flip ? len + 1 : len - 1;
        _flip = !_flip;
        return await conn.ExecuteAsync(
            "update film set length=@len where film_id=@id",
            new { id = _filmId, len = newLen });
    }

    [Benchmark]
    public async Task<int> InsertThenDeleteFilm_Mine()
    {
        _currentBenchmarkLabel = nameof(InsertThenDeleteFilm_Mine);
        var title = $"Bench_{Interlocked.Increment(ref _runCounter):D10}";

        var film = new Film { Title = title, Length = 123 };
        var created = await _filmHelper.CreateAsync(film, _ctx);
        if (!created) return 0;
        return await _filmHelper.DeleteAsync(film.Id, _ctx);
    }

    [Benchmark]
    public async Task<int> InsertThenDeleteFilm_Dapper()
    {
        _currentBenchmarkLabel = nameof(InsertThenDeleteFilm_Dapper);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var title = $"Bench_{Guid.NewGuid():N}";
        var id = await conn.ExecuteScalarAsync<int>(
            "insert into film(title, length) values (@t, @l) returning film_id",
            new { t = title, l = 123 });
        return await conn.ExecuteAsync("delete from film where film_id=@id", new { id });
    }


    [Benchmark]
    public async Task<List<Film>> GetTenFilms_Mine()
    {
        return await _filmHelper.RetrieveAsync(_filmIds10, _ctx);
    }

    [Benchmark]
    public async Task<List<Film>> GetTenFilms_Dapper()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = "select film_id as \"Id\", title as \"Title\", length as \"Length\" from film where film_id = any(@ids)";
        return (await conn.QueryAsync<Film>(sql, new { ids = _filmIds10.ToArray() })).ToList();
    }

    // Entity Framework Benchmarks
    [Benchmark]
    public async Task<EfFilm?> GetFilmById_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_EntityFramework);
        return await _efDbContext.Films.FirstOrDefaultAsync(f => f.Id == _filmId);
    }

    [Benchmark]
    public async Task<EfFilm?> GetFilmById_EntityFramework_NoTracking()
    {
        _currentBenchmarkLabel = nameof(GetFilmById_EntityFramework_NoTracking);
        return await _efDbContext.Films.AsNoTracking().FirstOrDefaultAsync(f => f.Id == _filmId);
    }

    [Benchmark]
    public async Task<EfFilmActor?> GetFilmActorComposite_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(GetFilmActorComposite_EntityFramework);
        return await _efDbContext.FilmActors
            .FirstOrDefaultAsync(fa => fa.ActorId == _compositeKey.actorId && fa.FilmId == _compositeKey.filmId);
    }

    [Benchmark]
    public async Task<EfFilmActor?> GetFilmActorComposite_EntityFramework_NoTracking()
    {
        _currentBenchmarkLabel = nameof(GetFilmActorComposite_EntityFramework_NoTracking);
        return await _efDbContext.FilmActors
            .AsNoTracking()
            .FirstOrDefaultAsync(fa => fa.ActorId == _compositeKey.actorId && fa.FilmId == _compositeKey.filmId);
    }

    [Benchmark]
    public async Task<int> UpdateFilm_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(UpdateFilm_EntityFramework);

        var film = await _efDbContext.Films.FirstAsync(f => f.Id == _filmId);

        // Toggle length to avoid no-op updates
        film.Length = _flip ? film.Length + 1 : film.Length - 1;
        _flip = !_flip;

        return await _efDbContext.SaveChangesAsync();
    }

    [Benchmark]
    public async Task<int> InsertThenDeleteFilm_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(InsertThenDeleteFilm_EntityFramework);
        var title = $"Bench_{Interlocked.Increment(ref _runCounter):D10}";

        var film = new EfFilm { Title = title, Length = 123 };
        _efDbContext.Films.Add(film);
        var insertResult = await _efDbContext.SaveChangesAsync();

        if (insertResult > 0)
        {
            _efDbContext.Films.Remove(film);
            return await _efDbContext.SaveChangesAsync();
        }

        return 0;
    }

    [Benchmark]
    public async Task<List<EfFilm>> GetTenFilms_EntityFramework()
    {
        _currentBenchmarkLabel = nameof(GetTenFilms_EntityFramework);
        return await _efDbContext.Films
            .Where(f => _filmIds10.Contains(f.Id))
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<EfFilm>> GetTenFilms_EntityFramework_NoTracking()
    {
        _currentBenchmarkLabel = nameof(GetTenFilms_EntityFramework_NoTracking);
        return await _efDbContext.Films
            .AsNoTracking()
            .Where(f => _filmIds10.Contains(f.Id))
            .ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
    }

    // Entities
    [Table("film", schema: "public")]
    public class Film
    {
        [Id(false)]
        [Column("film_id", DbType.Int32)]
        public int Id { get; set; }

        [Column("title", DbType.String)]
        public string Title { get; set; } = string.Empty;

        [Column("length", DbType.Int32)]
        public int Length { get; set; }
    }

    [Table("film_actor" , "public")]
    public class FilmActor
    {
        [pengdows.crud.attributes.PrimaryKey(1)]
        [Column("actor_id", DbType.Int32)]
        public int ActorId { get; set; }

        [pengdows.crud.attributes.PrimaryKey(2)]
        [Column("film_id", DbType.Int32)]
        public int FilmId { get; set; }
    }

    // Entity Framework DbContext and entities
    public class PagilaDbContext : DbContext
    {
        public PagilaDbContext(DbContextOptions<PagilaDbContext> options) : base(options) { }

        public DbSet<EfFilm> Films { get; set; }
        public DbSet<EfActor> Actors { get; set; }
        public DbSet<EfFilmActor> FilmActors { get; set; }

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
}
