using System.Data;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace CrudBenchmarks;

/// <summary>
/// Benchmark to measure the impact of column name caching optimization.
/// Compares direct dialect.WrapObjectName() vs cached BuildWrappedColumnName().
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ColumnCachingBenchmark
{
    private IDatabaseContext _context = null!;
    private TableGateway<TestEntity, long> _helper = null!;
    private ISqlDialect _dialect = null!;
    private List<long> _ids = null!;
    private List<TestEntity> _entities = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var map = new TypeMapRegistry();
        map.Register<TestEntity>();

        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        _context = new DatabaseContext("fake", factory, map);
        _helper = new TableGateway<TestEntity, long>(_context);
        _dialect = ((ISqlDialectProvider)_context.CreateSqlContainer()).Dialect;

        // Test data
        _ids = Enumerable.Range(1, 100).Select(i => (long)i).ToList();
        _entities = Enumerable.Range(1, 100).Select(i => new TestEntity
        {
            Id = i,
            Column1 = $"Value{i}",
            Column2 = $"Value{i}",
            Column3 = $"Value{i}",
            Column4 = $"Value{i}",
            Column5 = $"Value{i}",
            Column6 = $"Value{i}",
            Column7 = $"Value{i}",
            Column8 = $"Value{i}",
            Column9 = $"Value{i}",
            Column10 = $"Value{i}"
        }).ToList();
    }

    // ============================================================================
    // Scenario 1: BuildBaseRetrieve (SELECT with all columns)
    // ============================================================================

    [Benchmark(Baseline = true)]
    public string BuildBaseRetrieve_WithoutCache()
    {
        // Simulates current code after reversion
        var sc = _helper.BuildBaseRetrieve("e", _context);
        return sc.Query.ToString();
    }

    [Benchmark]
    public string BuildBaseRetrieve_Cached()
    {
        // This would use BuildWrappedColumnName internally
        // For now, measures the same thing to establish baseline
        var sc = _helper.BuildBaseRetrieve("e", _context);
        return sc.Query.ToString();
    }

    // ============================================================================
    // Scenario 2: BuildRetrieve with WHERE clause
    // ============================================================================

    [Benchmark]
    public string BuildRetrieve_WithoutCache()
    {
        var sc = _helper.BuildRetrieve(_ids, "e", _context);
        return sc.Query.ToString();
    }

    [Benchmark]
    public string BuildRetrieve_Cached()
    {
        var sc = _helper.BuildRetrieve(_ids, "e", _context);
        return sc.Query.ToString();
    }

    // ============================================================================
    // Scenario 3: BuildRetrieve by entities (uses BuildWhereByPrimaryKey)
    // ============================================================================

    [Benchmark]
    public string BuildRetrieveByEntity_WithoutCache()
    {
        var sc = _helper.BuildRetrieve(_entities, "e", _context);
        return sc.Query.ToString();
    }

    [Benchmark]
    public string BuildRetrieveByEntity_Cached()
    {
        var sc = _helper.BuildRetrieve(_entities, "e", _context);
        return sc.Query.ToString();
    }

    // ============================================================================
    // Scenario 4: Multiple operations (tests cache effectiveness)
    // ============================================================================

    [Benchmark]
    public int MultipleOperations_WithoutCache()
    {
        var count = 0;
        for (int i = 0; i < 10; i++)
        {
            var sc1 = _helper.BuildBaseRetrieve("e", _context);
            var sc2 = _helper.BuildRetrieve(_ids.Take(10).ToList(), "e", _context);
            var sc3 = _helper.BuildRetrieve(_entities.Take(5).ToList(), "e", _context);
            count += sc1.Query.Length + sc2.Query.Length + sc3.Query.Length;
        }
        return count;
    }

    [Benchmark]
    public int MultipleOperations_Cached()
    {
        var count = 0;
        for (int i = 0; i < 10; i++)
        {
            var sc1 = _helper.BuildBaseRetrieve("e", _context);
            var sc2 = _helper.BuildRetrieve(_ids.Take(10).ToList(), "e", _context);
            var sc3 = _helper.BuildRetrieve(_entities.Take(5).ToList(), "e", _context);
            count += sc1.Query.Length + sc2.Query.Length + sc3.Query.Length;
        }
        return count;
    }

    // ============================================================================
    // Direct comparison: WrapObjectName vs Cached wrapper
    // ============================================================================

    [Benchmark]
    public string DirectWrapping_10Columns()
    {
        var result = "";
        var columns = new[] { "column1", "column2", "column3", "column4", "column5",
                              "column6", "column7", "column8", "column9", "column10" };

        // Simulate wrapping each column 10 times (typical for complex query building)
        for (int iteration = 0; iteration < 10; iteration++)
        {
            foreach (var col in columns)
            {
                result = _dialect.WrapObjectName(col);
            }
        }
        return result;
    }

    [Benchmark]
    public string CachedWrapping_10Columns()
    {
        // This will measure after we re-apply caching
        // For now it's the same to establish baseline
        var result = "";
        var columns = new[] { "column1", "column2", "column3", "column4", "column5",
                              "column6", "column7", "column8", "column9", "column10" };

        for (int iteration = 0; iteration < 10; iteration++)
        {
            foreach (var col in columns)
            {
                result = _dialect.WrapObjectName(col);
            }
        }
        return result;
    }

    // ============================================================================
    // Test Entity with many columns
    // ============================================================================

    [Table("test_entity")]
    public class TestEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("column1", DbType.String)]
        public string Column1 { get; set; } = string.Empty;

        [Column("column2", DbType.String)]
        public string Column2 { get; set; } = string.Empty;

        [Column("column3", DbType.String)]
        public string Column3 { get; set; } = string.Empty;

        [Column("column4", DbType.String)]
        public string Column4 { get; set; } = string.Empty;

        [Column("column5", DbType.String)]
        public string Column5 { get; set; } = string.Empty;

        [Column("column6", DbType.String)]
        public string Column6 { get; set; } = string.Empty;

        [Column("column7", DbType.String)]
        public string Column7 { get; set; } = string.Empty;

        [Column("column8", DbType.String)]
        public string Column8 { get; set; } = string.Empty;

        [Column("column9", DbType.String)]
        public string Column9 { get; set; } = string.Empty;

        [Column("column10", DbType.String)]
        public string Column10 { get; set; } = string.Empty;
    }
}
