using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SqlGenerationBenchmark
{
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
        
        // Use FakeDb to avoid database connection issues in benchmarks
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        _ctx = new DatabaseContext("fake", factory, _map, DbMode.Standard);
        _filmHelper = new EntityHelper<Film, int>(_ctx);
        
        // EntityHelper will handle internal caching automatically
        
        // Static SQL for comparison (what Dapper essentially uses)
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
        // Test just getting the SQL string from newly built container
        var container = _filmHelper.BuildRetrieve(new[] { _filmId });
        return container.Query.ToString();
    }
    
    [Benchmark]
    public string SqlGeneration_Static()
    {
        // Baseline: static SQL (what Dapper essentially uses)
        return _staticSql;
    }

    [Benchmark]
    public ISqlContainer SqlGeneration_Mine_CreateContainer()
    {
        // Test creating an empty container
        return _ctx.CreateSqlContainer("SELECT * FROM film WHERE id = ");
    }

    [Benchmark]
    public ISqlContainer SqlGeneration_Mine_BuildUpdate()
    {
        // Uses EntityHelper's internal caching and optimized template generation
        var film = new Film { Id = _filmId, Title = "Test", Length = 120 };
        return _filmHelper.BuildUpdateAsync(film, false, _ctx).Result;
    }

    [Benchmark]
    public ISqlContainer SqlGeneration_Mine_BuildCreate()
    {
        // Uses EntityHelper's internal caching and optimized template generation
        var film = new Film { Id = _filmId, Title = "Test", Length = 120 };
        return _filmHelper.BuildCreate(film, _ctx);
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

    [Benchmark]
    public DbParameter ParameterCreation_Mine_String()
    {
        return _ctx.CreateDbParameter("title", DbType.String, "Test Film");
    }

    [Benchmark]
    public DbParameter ParameterCreation_Mine_Int()
    {
        return _ctx.CreateDbParameter("length", DbType.Int32, 120);
    }

    // ============= CONTAINER OPERATIONS =============

    [Benchmark]
    public void ContainerOperations_AddParameter()
    {
        var container = _ctx.CreateSqlContainer();
        container.AddParameterWithValue("p0", DbType.Int32, _filmId);
    }

    [Benchmark]
    public void ContainerOperations_BuildQuery()
    {
        var container = _ctx.CreateSqlContainer();
        container.Query.Append("SELECT * FROM film WHERE id = ");
        var param = container.MakeParameterName("id");
        container.Query.Append(param);
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
