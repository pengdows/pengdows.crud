using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Dapper;

// Uses FakeDb to isolate from external DB dependency while ensuring
// each framework uses its OWN native infrastructure for fair comparison

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class IsolationBenchmarks
{
    // Use FakeDb to isolate from external dependencies
    private string _connStr = "fake";
    private IDatabaseContext _ctx = null!;
    private TypeMapRegistry _map = null!;
    private EntityHelper<Film, int> _filmHelper = null!;

    // Dapper uses its OWN factory and connections - NOT pengdows.crud infrastructure
    private fakeDbFactory _dapperFactory = null!;

    private int _filmId = 1;
    private string _staticSql = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _map = new TypeMapRegistry();
        _map.Register<Film>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _connStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        // pengdows.crud uses its own factory instance
        var pengdowsFactory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        _ctx = new DatabaseContext(cfg, pengdowsFactory, null, _map);
        _filmHelper = new EntityHelper<Film, int>(_ctx);

        // Dapper gets its OWN factory instance - completely separate from pengdows.crud
        _dapperFactory = new fakeDbFactory(SupportedDatabase.PostgreSql);

        // EntityHelper will handle internal caching automatically

        // Static SQL for comparison
        _staticSql =
            "SELECT \"film_id\", \"title\", \"length\" FROM \"public\".\"film\" WHERE \"film_id\" = ANY(@ids)";
    }

    // ============= SQL GENERATION BENCHMARKS =============

    [Benchmark(Baseline = true)]
    public ISqlContainer SqlGeneration_Mine_BuildContainer()
    {
        // This tests pure SQL generation overhead
        return _filmHelper.BuildRetrieve(new[] { _filmId });
    }

    [Benchmark]
    public string SqlGeneration_Mine_GetSql()
    {
        // Test just getting the SQL string from pre-built container
        var container = _filmHelper.BuildRetrieve(new[] { _filmId });
        return container.Query.ToString();
    }

    [Benchmark]
    public string SqlGeneration_Static()
    {
        // Baseline: static SQL (what Dapper essentially uses)
        return _staticSql;
    }

    // ============= OBJECT LOADING BENCHMARKS =============

    [Benchmark]
    public async Task<Film?> ObjectLoading_Mine()
    {
        // Integrated path: EntityHelper handles caching/optimization internally
        return await _filmHelper.RetrieveOneAsync(_filmId, _ctx);
    }

    [Benchmark]
    public async Task<Film?> ObjectLoading_Mine_DirectReader()
    {
        // Test just the object mapping part by using direct SQL
        await using var conn = _ctx.GetConnection(ExecutionType.Read);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _staticSql;
        var param = cmd.CreateParameter();
        param.ParameterName = "ids";
        param.Value = new[] { _filmId };
        cmd.Parameters.Add(param);

        await conn.OpenAsync();
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return _filmHelper.MapReaderToObject((ITrackedReader)reader);
        }

        return null;
    }

    [Benchmark]
    public async Task<Film?> ObjectLoading_Dapper()
    {
        // Dapper uses its OWN factory and connection - NOT pengdows.crud infrastructure
        await using var conn = _dapperFactory.CreateConnection();
        conn.ConnectionString = _connStr;
        await conn.OpenAsync();
        var row = await conn.QuerySingleOrDefaultAsync<DapperFilmRow>(_staticSql, new { ids = new[] { _filmId } });
        if (row == null)
        {
            return null;
        }

        // Map to a new object (Dapper handles its own mapping)
        return new Film
        {
            Id = row.film_id,
            Title = row.title,
            Length = row.length
        };
    }

    // ============= PARAMETER CREATION BENCHMARKS =============

    [Benchmark]
    public DbParameter ParameterCreation_Mine()
    {
        return _ctx.CreateDbParameter("p0", DbType.Object, new[] { _filmId });
    }

    [Benchmark]
    public object ParameterCreation_Dapper()
    {
        var parameters = new DynamicParameters();
        parameters.Add("id", _filmId, DbType.Int32);
        return parameters;
    }

    // ============= CONNECTION OVERHEAD BENCHMARKS =============

    [Benchmark]
    public async Task<ITrackedConnection> ConnectionOverhead_Mine()
    {
        // pengdows.crud connection management (with tracking, wrapping, etc.)
        var conn = _ctx.GetConnection(ExecutionType.Read);
        await conn.OpenAsync();
        _ctx.CloseAndDisposeConnection(conn);
        return conn;
    }

    [Benchmark]
    public async Task<DbConnection> ConnectionOverhead_Direct()
    {
        // Direct ADO.NET/Dapper style - uses factory directly, NO pengdows.crud infrastructure
        var conn = _dapperFactory.CreateConnection();
        conn.ConnectionString = _connStr;
        await conn.OpenAsync();
        conn.Close();
        conn.Dispose();
        return conn;
    }

    // Dapper-specific DTO - no pengdows.crud attributes
    private sealed record DapperFilmRow(int film_id, string title, int length);

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
