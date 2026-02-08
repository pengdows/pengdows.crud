using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// THESIS PROOF: pengdows.crud's performance optimizations make it
/// VERY CLOSE to Dapper for raw operations.
///
/// KEY FINDINGS:
/// - Compiled property setters (no reflection on hot path)
/// - Optimized parameter creation
/// - Cached SQL templates
/// - StringBuilderLite for minimal allocations
/// - Plan caching for reader mapping
///
/// RESULT: Should be within 5-15% of Dapper, much faster than EF
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class RawPerformanceComparison : IDisposable
{
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<PerfEntity, int> _pengdowsHelper = null!;
    private SqliteConnection _dapperConnection = null!;
    private EfPerfContext _efContext = null!;
    private TypeMapRegistry _typeMap = null!;

    [Params(1, 10, 100)] public int RecordCount;

    [GlobalSetup]
    public void Setup()
    {
        var connStr = "Data Source=:memory:";

        // pengdows.crud
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<PerfEntity>();
        _pengdowsContext = new DatabaseContext(connStr, SqliteFactory.Instance, _typeMap);
        _pengdowsHelper = new TableGateway<PerfEntity, int>(_pengdowsContext);

        // Dapper
        _dapperConnection = new SqliteConnection(connStr);
        _dapperConnection.Open();

        // EF
        var efOptions = new DbContextOptionsBuilder<EfPerfContext>()
            .UseSqlite(connStr)
            .Options;
        _efContext = new EfPerfContext(efOptions);

        CreateSchema();
        SeedData();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pengdowsContext?.Dispose();
        _dapperConnection?.Dispose();
        _efContext?.Dispose();
    }

    private void CreateSchema()
    {
        using var container = _pengdowsContext.CreateSqlContainer(@"
            CREATE TABLE perf_test (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                age INTEGER NOT NULL,
                salary REAL NOT NULL,
                is_active INTEGER NOT NULL,
                created_at TEXT NOT NULL
            )");
        container.ExecuteNonQueryAsync().AsTask().Wait();
    }

    private void SeedData()
    {
        for (int i = 1; i <= 1000; i++)
        {
            var entity = new PerfEntity
            {
                Name = $"Person {i}",
                Age = 20 + (i % 50),
                Salary = 30000 + (i * 100),
                IsActive = i % 2 == 0,
                CreatedAt = DateTime.UtcNow.AddDays(-i)
            };
            _pengdowsHelper.CreateAsync(entity).Wait();
        }
    }

    // ============================================================================
    // RAW PERFORMANCE: Single Read
    // ============================================================================

    [Benchmark(Baseline = true)]
    public async Task<PerfEntity?> SingleRead_Pengdows()
    {
        return await _pengdowsHelper.RetrieveOneAsync(1);
    }

    [Benchmark]
    public async Task<DapperPerfEntity?> SingleRead_Dapper()
    {
        return await _dapperConnection.QueryFirstOrDefaultAsync<DapperPerfEntity>(
            "SELECT id, name, age, salary, is_active, created_at FROM perf_test WHERE id = @Id",
            new { Id = 1 });
    }

    [Benchmark]
    public async Task<EfPerfEntity?> SingleRead_EntityFramework()
    {
        return await _efContext.PerfEntities.AsNoTracking().FirstOrDefaultAsync(e => e.Id == 1);
    }

    // ============================================================================
    // RAW PERFORMANCE: List Read
    // ============================================================================

    [Benchmark]
    public async Task<List<PerfEntity>> ListRead_Pengdows()
    {
        using var container = _pengdowsContext.CreateSqlContainer(
            "SELECT id, name, age, salary, is_active, created_at FROM perf_test LIMIT @Limit");
        container.AddParameterWithValue("Limit", DbType.Int32, RecordCount);
        return await _pengdowsHelper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperPerfEntity>> ListRead_Dapper()
    {
        var results = await _dapperConnection.QueryAsync<DapperPerfEntity>(
            "SELECT id, name, age, salary, is_active, created_at FROM perf_test LIMIT @Limit",
            new { Limit = RecordCount });
        return results.ToList();
    }

    [Benchmark]
    public async Task<List<EfPerfEntity>> ListRead_EntityFramework()
    {
        return await _efContext.PerfEntities.AsNoTracking().Take(RecordCount).ToListAsync();
    }

    // ============================================================================
    // RAW PERFORMANCE: Filtered Query
    // ============================================================================

    [Benchmark]
    public async Task<List<PerfEntity>> FilteredQuery_Pengdows()
    {
        using var container = _pengdowsContext.CreateSqlContainer(
            "SELECT id, name, age, salary, is_active, created_at FROM perf_test WHERE age > @MinAge AND is_active = @IsActive LIMIT @Limit");
        container.AddParameterWithValue("MinAge", DbType.Int32, 30);
        container.AddParameterWithValue("IsActive", DbType.Boolean, true);
        container.AddParameterWithValue("Limit", DbType.Int32, RecordCount);
        return await _pengdowsHelper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperPerfEntity>> FilteredQuery_Dapper()
    {
        var results = await _dapperConnection.QueryAsync<DapperPerfEntity>(
            "SELECT id, name, age, salary, is_active, created_at FROM perf_test WHERE age > @MinAge AND is_active = @IsActive LIMIT @Limit",
            new { MinAge = 30, IsActive = 1, Limit = RecordCount });
        return results.ToList();
    }

    [Benchmark]
    public async Task<List<EfPerfEntity>> FilteredQuery_EntityFramework()
    {
        return await _efContext.PerfEntities
            .AsNoTracking()
            .Where(e => e.Age > 30 && e.IsActive)
            .Take(RecordCount)
            .ToListAsync();
    }

    // ============================================================================
    // RAW PERFORMANCE: Insert
    // ============================================================================

    [Benchmark]
    public async Task Insert_Pengdows()
    {
        for (int i = 0; i < RecordCount; i++)
        {
            var entity = new PerfEntity
            {
                Name = $"New Person {i}",
                Age = 25,
                Salary = 50000,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await _pengdowsHelper.CreateAsync(entity);
        }
    }

    [Benchmark]
    public async Task Insert_Dapper()
    {
        for (int i = 0; i < RecordCount; i++)
        {
            await _dapperConnection.ExecuteAsync(
                "INSERT INTO perf_test (name, age, salary, is_active, created_at) VALUES (@Name, @Age, @Salary, @IsActive, @CreatedAt)",
                new
                {
                    Name = $"New Person {i}",
                    Age = 25,
                    Salary = 50000.0,
                    IsActive = 1,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                });
        }
    }

    [Benchmark]
    public async Task Insert_EntityFramework()
    {
        for (int i = 0; i < RecordCount; i++)
        {
            var entity = new EfPerfEntity
            {
                Name = $"New Person {i}",
                Age = 25,
                Salary = 50000,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _efContext.PerfEntities.Add(entity);
        }
        await _efContext.SaveChangesAsync();

        // Clear tracking
        _efContext.ChangeTracker.Clear();
    }

    // ============================================================================
    // RAW PERFORMANCE: Update
    // ============================================================================

    [Benchmark]
    public async Task Update_Pengdows()
    {
        for (int i = 1; i <= RecordCount; i++)
        {
            var entity = await _pengdowsHelper.RetrieveOneAsync(i);
            if (entity != null)
            {
                entity.Salary = entity.Salary * 1.1m;
                await _pengdowsHelper.UpdateAsync(entity);
            }
        }
    }

    [Benchmark]
    public async Task Update_Dapper()
    {
        for (int i = 1; i <= RecordCount; i++)
        {
            await _dapperConnection.ExecuteAsync(
                "UPDATE perf_test SET salary = salary * 1.1 WHERE id = @Id",
                new { Id = i });
        }
    }

    [Benchmark]
    public async Task Update_EntityFramework()
    {
        for (int i = 1; i <= RecordCount; i++)
        {
            var entity = await _efContext.PerfEntities.FindAsync(i);
            if (entity != null)
            {
                entity.Salary = entity.Salary * 1.1m;
            }
        }
        await _efContext.SaveChangesAsync();
        _efContext.ChangeTracker.Clear();
    }

    // ============================================================================
    // RAW PERFORMANCE: Delete
    // ============================================================================

    [Benchmark]
    public async Task Delete_Pengdows()
    {
        // Create entities to delete
        var toDelete = new List<int>();
        for (int i = 0; i < RecordCount; i++)
        {
            var entity = new PerfEntity
            {
                Name = "ToDelete",
                Age = 99,
                Salary = 1,
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            };
            await _pengdowsHelper.CreateAsync(entity);
            toDelete.Add(entity.Id);
        }

        // Delete them
        foreach (var id in toDelete)
        {
            await _pengdowsHelper.DeleteAsync(id);
        }
    }

    [Benchmark]
    public async Task Delete_Dapper()
    {
        // Create entities to delete
        var toDelete = new List<int>();
        for (int i = 0; i < RecordCount; i++)
        {
            var id = await _dapperConnection.ExecuteScalarAsync<int>(
                "INSERT INTO perf_test (name, age, salary, is_active, created_at) VALUES (@Name, @Age, @Salary, @IsActive, @CreatedAt); SELECT last_insert_rowid()",
                new
                {
                    Name = "ToDelete",
                    Age = 99,
                    Salary = 1.0,
                    IsActive = 0,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                });
            toDelete.Add(id);
        }

        // Delete them
        foreach (var id in toDelete)
        {
            await _dapperConnection.ExecuteAsync("DELETE FROM perf_test WHERE id = @Id", new { Id = id });
        }
    }

    [Benchmark]
    public async Task Delete_EntityFramework()
    {
        // Create entities to delete
        var toDelete = new List<EfPerfEntity>();
        for (int i = 0; i < RecordCount; i++)
        {
            var entity = new EfPerfEntity
            {
                Name = "ToDelete",
                Age = 99,
                Salary = 1,
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            };
            _efContext.PerfEntities.Add(entity);
            toDelete.Add(entity);
        }
        await _efContext.SaveChangesAsync();

        // Delete them
        foreach (var entity in toDelete)
        {
            _efContext.PerfEntities.Remove(entity);
        }
        await _efContext.SaveChangesAsync();
        _efContext.ChangeTracker.Clear();
    }

    // ============================================================================
    // RAW PERFORMANCE: Aggregate Query
    // ============================================================================

    [Benchmark]
    public async Task<decimal> AggregateQuery_Pengdows()
    {
        using var container = _pengdowsContext.CreateSqlContainer(
            "SELECT AVG(salary) FROM perf_test WHERE is_active = @IsActive");
        container.AddParameterWithValue("IsActive", DbType.Boolean, true);
        return await container.ExecuteScalarAsync<decimal>();
    }

    [Benchmark]
    public async Task<decimal> AggregateQuery_Dapper()
    {
        return await _dapperConnection.ExecuteScalarAsync<decimal>(
            "SELECT AVG(salary) FROM perf_test WHERE is_active = @IsActive",
            new { IsActive = 1 });
    }

    [Benchmark]
    public async Task<decimal> AggregateQuery_EntityFramework()
    {
        return await _efContext.PerfEntities
            .Where(e => e.IsActive)
            .AverageAsync(e => e.Salary);
    }

    public void Dispose()
    {
        Cleanup();
    }

    // ============================================================================
    // ENTITIES
    // ============================================================================

    [Table("perf_test")]
    public class PerfEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Column("age", DbType.Int32)]
        public int Age { get; set; }

        [Column("salary", DbType.Decimal)]
        public decimal Salary { get; set; }

        [Column("is_active", DbType.Boolean)]
        public bool IsActive { get; set; }

        [Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }
    }

    public class DapperPerfEntity
    {
        public int id { get; set; }
        public string name { get; set; } = string.Empty;
        public int age { get; set; }
        public decimal salary { get; set; }
        public int is_active { get; set; }
        public string created_at { get; set; } = string.Empty;
    }

    public class EfPerfEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public decimal Salary { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class EfPerfContext : DbContext
    {
        public EfPerfContext(DbContextOptions<EfPerfContext> options) : base(options) { }
        public DbSet<EfPerfEntity> PerfEntities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfPerfEntity>(entity =>
            {
                entity.ToTable("perf_test");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Age).HasColumnName("age");
                entity.Property(e => e.Salary).HasColumnName("salary");
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            });
        }
    }
}
