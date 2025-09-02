using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using pengdows.crud;
using pengdows.crud.attributes;

namespace CrudBenchmarks;

[MemoryDiagnoser]
public class PagilaBenchmarks : IAsyncDisposable
{
    private IContainer? _container;
    private string _connStr = string.Empty;
    private IDatabaseContext _ctx = null!;
    private TypeMapRegistry _map = null!;
    private EntityHelper<Film, int> _filmHelper = null!;
    private EntityHelper<FilmActor, int> _filmActorHelper = null!;

    [Params(1000)]
    public int FilmCount;

    [Params(200)]
    public int ActorCount;

    private int _filmId;
    private (int actorId, int filmId) _compositeKey;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithEnvironment("POSTGRES_DB", "pagila")
            .WithPortBinding(0, 5432)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _container.StartAsync();

        var mappedPort = _container.GetMappedPublicPort(5432);
        _connStr = $"Host=localhost;Port={mappedPort};Database=pagila;Username=postgres;Password=postgres;Maximum Pool Size=100";

        await WaitForReady();
        await CreateSchemaAndSeedAsync();

        _map = new TypeMapRegistry();
        _map.Register<Film>();
        _map.Register<FilmActor>();
        _ctx = new DatabaseContext(_connStr, NpgsqlFactory.Instance, _map);

        _filmHelper = new EntityHelper<Film, int>(_ctx);
        _filmActorHelper = new EntityHelper<FilmActor, int>(_ctx);

        // pick keys to use in benchmarks
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        _filmId = await conn.ExecuteScalarAsync<int>("select film_id from film order by film_id limit 1");
        var row = await conn.QuerySingleAsync<(int actor_id, int film_id)>("select actor_id, film_id from film_actor limit 1");
        _compositeKey = (row.actor_id, row.film_id);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_ctx is IAsyncDisposable ad)
        {
            await ad.DisposeAsync();
        }
        if (_container is not null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
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
        var sc = _filmHelper.BuildRetrieve(new[] { _filmId });
        return await _filmHelper.LoadSingleAsync(sc);
    }

    [Benchmark]
    public async Task<Film?> GetFilmById_Dapper()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        return await conn.QuerySingleOrDefaultAsync<Film>(
            "select film_id as \"Id\", title as \"Title\", length as \"Length\" from film where film_id=@id",
            new { id = _filmId });
    }

    [Benchmark]
    public async Task<FilmActor?> GetFilmActorComposite_Mine()
    {
        var sc = _filmActorHelper.BuildRetrieve(new[] { new FilmActor { ActorId = _compositeKey.actorId, FilmId = _compositeKey.filmId } });
        return await _filmActorHelper.LoadSingleAsync(sc);
    }

    [Benchmark]
    public async Task<FilmActor?> GetFilmActorComposite_Dapper()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        return await conn.QuerySingleOrDefaultAsync<FilmActor>(
            "select actor_id as \"ActorId\", film_id as \"FilmId\" from film_actor where actor_id=@a and film_id=@f",
            new { a = _compositeKey.actorId, f = _compositeKey.filmId });
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
    }

    // Entities
    [Table("film")]
    public class Film
    {
        [Id]
        [Column("film_id", DbType.Int32)]
        public int Id { get; set; }

        [Column("title", DbType.String)]
        public string Title { get; set; } = string.Empty;

        [Column("length", DbType.Int32)]
        public int Length { get; set; }
    }

    [Table("film_actor")]
    public class FilmActor
    {
        [PrimaryKey(1)]
        [Column("actor_id", DbType.Int32)]
        public int ActorId { get; set; }

        [PrimaryKey(2)]
        [Column("film_id", DbType.Int32)]
        public int FilmId { get; set; }
    }
}

