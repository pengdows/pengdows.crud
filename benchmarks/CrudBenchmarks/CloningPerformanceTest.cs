using System.Data;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class CloningPerformanceTest
{
    private IDatabaseContext _ctx = null!;
    private TypeMapRegistry _map = null!;
    private TableGateway<Film, int> _filmHelper = null!;

    private int _filmId = 1;
    private ISqlContainer _cachedContainer = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _map = new TypeMapRegistry();
        _map.Register<Film>();

        // Use FakeDb
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "fake",
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _ctx = new DatabaseContext(cfg, factory, null, _map);
        _filmHelper = new TableGateway<Film, int>(_ctx);

        // Pre-build container for cloning tests
        _cachedContainer = _filmHelper.BuildRetrieve(new[] { _filmId });
    }

    [Benchmark(Baseline = true)]
    public ISqlContainer BuildRetrieve_Traditional()
    {
        // Traditional approach: Build container from scratch each time
        return _filmHelper.BuildRetrieve(new[] { _filmId });
    }

    [Benchmark]
    public ISqlContainer BuildRetrieve_WithCloning()
    {
        // New approach: Clone cached container and update parameter
        var clone = _cachedContainer.Clone();
        clone.SetParameterValue("w0", _filmId);
        return clone;
    }

    [Benchmark]
    public ISqlContainer BasicSqlContainer()
    {
        // Baseline: Just create empty container
        return _ctx.CreateSqlContainer("SELECT * FROM film WHERE id = $1");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _cachedContainer?.Dispose();
        _ctx?.Dispose();
    }

    [Table("film", "public")]
    public class Film
    {
        [Id(false)]
        [Column("film_id", DbType.Int32)]
        public int Id { get; set; }

        [Column("title", DbType.String)] public string Title { get; set; } = string.Empty;

        [Column("length", DbType.Int32)] public int Length { get; set; }
    }
}