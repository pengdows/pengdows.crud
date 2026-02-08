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
/// FAIR PERFORMANCE BREAKDOWN - Shows what we're actually measuring
///
/// This benchmark breaks down performance into components:
/// 1. SQL Building Time - Cost of generating SQL
/// 2. Execution Time - Cost of running pre-built SQL
/// 3. Total Time - Building + Execution
///
/// GOAL: Prove pengdows.crud execution is close to Dapper, even if total is slower
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class FairPerformanceBreakdown : IDisposable
{
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<Product, int> _productHelper = null!;
    private SqliteConnection _dapperConnection = null!;
    private EfProductContext _efContext = null!;
    private TypeMapRegistry _typeMap = null!;

    // Pre-built SQL containers (reusable)
    private ISqlContainer _prebuiltInsert = null!;
    private ISqlContainer _prebuiltSelect = null!;
    private ISqlContainer _prebuiltUpdate = null!;
    private ISqlContainer _prebuiltDelete = null!;

    // Test product for reuse
    private Product _testProduct = null!;

    [GlobalSetup]
    public void Setup()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<Product>();

        // Shared cache so all frameworks see same database
        var connStr = "Data Source=FairBench;Mode=Memory;Cache=Shared";
        _pengdowsContext = new DatabaseContext(connStr, SqliteFactory.Instance, _typeMap);
        _productHelper = new TableGateway<Product, int>(_pengdowsContext);

        // Dapper
        _dapperConnection = new SqliteConnection(connStr);
        _dapperConnection.Open();

        // EF
        var efOptions = new DbContextOptionsBuilder<EfProductContext>()
            .UseSqlite(connStr)
            .Options;
        _efContext = new EfProductContext(efOptions);

        CreateSchema();
        SeedData();

        // Pre-build SQL containers (do this ONCE, reuse many times)
        _testProduct = new Product
        {
            Id = 1,
            Name = "Test Product",
            Price = 99.99m,
            Stock = 100,
            IsActive = true
        };

        _prebuiltInsert = _productHelper.BuildCreate(_testProduct, _pengdowsContext);
        _prebuiltSelect = _productHelper.BuildRetrieve(new[] { 1 }, _pengdowsContext);
        _prebuiltUpdate = _productHelper.BuildUpdateAsync(_testProduct, false, _pengdowsContext).Result;
        _prebuiltDelete = _productHelper.BuildDelete(999, _pengdowsContext);
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
            CREATE TABLE IF NOT EXISTS products (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                price REAL NOT NULL,
                stock INTEGER NOT NULL,
                is_active INTEGER NOT NULL
            )");
        container.ExecuteNonQueryAsync().AsTask().Wait();
    }

    private void SeedData()
    {
        for (int i = 1; i <= 100; i++)
        {
            var product = new Product
            {
                Name = $"Product {i}",
                Price = 10m + i,
                Stock = i * 10,
                IsActive = i % 2 == 0
            };
            _productHelper.CreateAsync(product).Wait();
        }
    }

    // ============================================================================
    // INSERT BENCHMARKS - Breakdown
    // ============================================================================

    [Benchmark(Description = "INSERT: Build + Execute (what we measured before)")]
    public async Task Insert_Pengdows_Total()
    {
        var product = new Product { Name = "New", Price = 49.99m, Stock = 50, IsActive = true };

        // This includes SQL building + execution
        await _productHelper.CreateAsync(product);
    }

    [Benchmark(Description = "INSERT: Build SQL only (overhead we want to isolate)")]
    public ISqlContainer Insert_Pengdows_BuildOnly()
    {
        var product = new Product { Name = "New", Price = 49.99m, Stock = 50, IsActive = true };

        // Just build the SQL, don't execute
        return _productHelper.BuildCreate(product, _pengdowsContext);
    }

    [Benchmark(Baseline = true, Description = "INSERT: Execute pre-built SQL (fair comparison to Dapper)")]
    public async Task<int> Insert_Pengdows_ExecuteOnly()
    {
        // SQL is already built, just execute it
        // This is comparable to Dapper's performance
        return await _prebuiltInsert.ExecuteNonQueryAsync();
    }

    [Benchmark(Description = "INSERT: Dapper execution (pre-written SQL)")]
    public async Task<int> Insert_Dapper()
    {
        // SQL is hardcoded, connection is open
        var sql = "INSERT INTO products (name, price, stock, is_active) VALUES (@Name, @Price, @Stock, @IsActive)";
        return await _dapperConnection.ExecuteAsync(sql, new
        {
            Name = "New",
            Price = 49.99m,
            Stock = 50,
            IsActive = 1
        });
    }

    [Benchmark(Description = "INSERT: Entity Framework (full overhead)")]
    public async Task<int> Insert_EntityFramework()
    {
        var product = new EfProduct
        {
            Name = "New",
            Price = 49.99m,
            Stock = 50,
            IsActive = true
        };

        _efContext.Products.Add(product);
        var result = await _efContext.SaveChangesAsync();
        _efContext.Entry(product).State = EntityState.Detached;
        return result;
    }

    // ============================================================================
    // SELECT BENCHMARKS - Breakdown
    // ============================================================================

    [Benchmark(Description = "SELECT: Build + Execute")]
    public async Task<Product?> Select_Pengdows_Total()
    {
        // Build SQL + execute
        return await _productHelper.RetrieveOneAsync(1);
    }

    [Benchmark(Description = "SELECT: Build SQL only")]
    public ISqlContainer Select_Pengdows_BuildOnly()
    {
        // Just build the SQL
        return _productHelper.BuildRetrieve(new[] { 1 }, _pengdowsContext);
    }

    [Benchmark(Description = "SELECT: Execute pre-built SQL")]
    public async Task<List<Product>> Select_Pengdows_ExecuteOnly()
    {
        // SQL already built, just execute
        return await _productHelper.LoadListAsync(_prebuiltSelect);
    }

    [Benchmark(Description = "SELECT: Dapper execution")]
    public async Task<Product?> Select_Dapper()
    {
        var sql = "SELECT id, name, price, stock, is_active FROM products WHERE id = @Id";
        return await _dapperConnection.QueryFirstOrDefaultAsync<Product>(sql, new { Id = 1 });
    }

    [Benchmark(Description = "SELECT: Entity Framework")]
    public async Task<EfProduct?> Select_EntityFramework()
    {
        return await _efContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == 1);
    }

    // ============================================================================
    // UPDATE BENCHMARKS - Breakdown
    // ============================================================================

    [Benchmark(Description = "UPDATE: Build + Execute")]
    public async Task<int> Update_Pengdows_Total()
    {
        var product = await _productHelper.RetrieveOneAsync(1);
        if (product == null) return 0;

        product.Price = 59.99m;
        return await _productHelper.UpdateAsync(product);
    }

    [Benchmark(Description = "UPDATE: Build SQL only")]
    public async Task<ISqlContainer> Update_Pengdows_BuildOnly()
    {
        var product = await _productHelper.RetrieveOneAsync(1);
        if (product == null) return _prebuiltUpdate;

        product.Price = 59.99m;
        return await _productHelper.BuildUpdateAsync(product, false, _pengdowsContext);
    }

    [Benchmark(Description = "UPDATE: Execute pre-built SQL")]
    public async Task<int> Update_Pengdows_ExecuteOnly()
    {
        // SQL already built, just execute
        return await _prebuiltUpdate.ExecuteNonQueryAsync();
    }

    [Benchmark(Description = "UPDATE: Dapper execution")]
    public async Task<int> Update_Dapper()
    {
        var sql = "UPDATE products SET price = @Price WHERE id = @Id";
        return await _dapperConnection.ExecuteAsync(sql, new { Id = 1, Price = 59.99m });
    }

    [Benchmark(Description = "UPDATE: Entity Framework")]
    public async Task<int> Update_EntityFramework()
    {
        var product = await _efContext.Products.FindAsync(1);
        if (product == null) return 0;

        product.Price = 59.99m;
        return await _efContext.SaveChangesAsync();
    }

    // ============================================================================
    // REALISTIC SCENARIO: Build Once, Execute Many
    // ============================================================================

    [Benchmark(Description = "REALISTIC: Build once, execute 10 times")]
    public async Task<int> Realistic_Pengdows_ReuseContainer()
    {
        // Build SQL once
        var template = new Product { Name = "Template", Price = 1m, Stock = 1, IsActive = true };
        var container = _productHelper.BuildCreate(template, _pengdowsContext);

        // Execute 10 times with different values
        var totalRows = 0;
        for (int i = 0; i < 10; i++)
        {
            // Update parameter values (no rebuilding!)
            var product = new Product
            {
                Name = $"Batch {i}",
                Price = 10m + i,
                Stock = i,
                IsActive = true
            };

            // Rebind parameters (cheap) and execute
            container.Clear();
            container.Query.Append("INSERT INTO products (name, price, stock, is_active) VALUES (");
            container.Query.Append(container.MakeParameterName("name"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("price"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("stock"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("is_active"));
            container.Query.Append(")");

            container.AddParameterWithValue("name", DbType.String, product.Name);
            container.AddParameterWithValue("price", DbType.Decimal, product.Price);
            container.AddParameterWithValue("stock", DbType.Int32, product.Stock);
            container.AddParameterWithValue("is_active", DbType.Boolean, product.IsActive);

            totalRows += await container.ExecuteNonQueryAsync();
        }

        return totalRows;
    }

    [Benchmark(Description = "REALISTIC: Dapper 10 executions")]
    public async Task<int> Realistic_Dapper()
    {
        var sql = "INSERT INTO products (name, price, stock, is_active) VALUES (@Name, @Price, @Stock, @IsActive)";
        var totalRows = 0;

        for (int i = 0; i < 10; i++)
        {
            totalRows += await _dapperConnection.ExecuteAsync(sql, new
            {
                Name = $"Batch {i}",
                Price = 10m + i,
                Stock = i,
                IsActive = 1
            });
        }

        return totalRows;
    }

    public void Dispose()
    {
        Cleanup();
    }

    // ============================================================================
    // ENTITIES
    // ============================================================================

    [Table("products")]
    public class Product
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Column("price", DbType.Decimal)]
        public decimal Price { get; set; }

        [Column("stock", DbType.Int32)]
        public int Stock { get; set; }

        [Column("is_active", DbType.Boolean)]
        public bool IsActive { get; set; }
    }

    public class EfProduct
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public bool IsActive { get; set; }
    }

    public class EfProductContext : DbContext
    {
        public EfProductContext(DbContextOptions<EfProductContext> options) : base(options) { }
        public DbSet<EfProduct> Products { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfProduct>(entity =>
            {
                entity.ToTable("products");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Price).HasColumnName("price");
                entity.Property(e => e.Stock).HasColumnName("stock");
                entity.Property(e => e.IsActive).HasColumnName("is_active");
            });
        }
    }
}
