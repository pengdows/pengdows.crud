using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using Dapper;
using Npgsql;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.wrappers;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class IsolationBenchmarks
{
    private string _connStr = "Host=localhost;Port=5432;Database=pagila;Username=postgres;Password=postgres;Maximum Pool Size=100";
    private IDatabaseContext _ctx = null!;
    private TypeMapRegistry _map = null!;
    private EntityHelper<Film, int> _filmHelper = null!;
    
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
        _ctx = new DatabaseContext(cfg, NpgsqlFactory.Instance, null, _map);
        _filmHelper = new EntityHelper<Film, int>(_ctx);
        
        // EntityHelper will handle internal caching automatically
        
        // Static SQL for comparison
        _staticSql = "SELECT \"film_id\", \"title\", \"length\" FROM \"public\".\"film\" WHERE \"film_id\" = ANY($1)";
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
    public async Task<Film?> ObjectLoading_Mine_Traditional()
    {
        // Traditional approach: SQL generation + execution + object loading
        var sc = _filmHelper.BuildRetrieve(new[] { _filmId });
        return await _filmHelper.LoadSingleAsync(sc);
    }
    
    [Benchmark]
    public async Task<Film?> ObjectLoading_Mine_FastPath()
    {
        // Fast-path: EntityHelper's built-in caching with cloning
        return await _filmHelper.RetrieveOneAsync(_filmId, _ctx);
    }
    
    [Benchmark]
    public async Task<Film?> ObjectLoading_Mine_DirectReader()
    {
        // Test just the object mapping part by using direct SQL
        using var conn = _ctx.GetConnection(ExecutionType.Read);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _staticSql;
        var param = cmd.CreateParameter();
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
        // Dapper baseline for comparison
        await using var conn = new NpgsqlConnection(_connStr);
        return await conn.QuerySingleOrDefaultAsync<Film>(
            "select film_id as \"Id\", title as \"Title\", length as \"Length\" from film where film_id=@id",
            new { id = _filmId });
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
        return new { id = _filmId };
    }

    // ============= CONNECTION OVERHEAD BENCHMARKS =============
    
    [Benchmark]
    public async Task<ITrackedConnection> ConnectionOverhead_Mine()
    {
        var conn = _ctx.GetConnection(ExecutionType.Read);
        await conn.OpenAsync();
        conn.Close();
        _ctx.CloseAndDisposeConnection(conn);
        return conn;
    }
    
    [Benchmark]
    public async Task<NpgsqlConnection> ConnectionOverhead_Direct()
    {
        var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await conn.CloseAsync();
        await conn.DisposeAsync();
        return conn;
    }

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
}