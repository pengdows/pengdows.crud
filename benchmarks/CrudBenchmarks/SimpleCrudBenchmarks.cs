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
/// Simple, focused benchmark comparing basic CRUD operations across:
/// - pengdows.crud
/// - Dapper
/// - Entity Framework Core
///
/// Uses SQLite (in-memory) for fast, no-dependency benchmarking.
/// Measures the core operations: Create, Read (single), Read (list), Update, Delete.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class SimpleCrudBenchmarks : IDisposable
{
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<Product, int> _productHelper = null!;
    private SqliteConnection _dapperConnection = null!;
    private EfProductContext _efContext = null!;
    private TypeMapRegistry _typeMap = null!;

    // Test data
    private Product _testProduct = null!;
    private List<Product> _testProducts = null!;
    private const int BatchSize = 100;

    [GlobalSetup]
    public void Setup()
    {
        // Setup pengdows.crud with SQLite
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<Product>();
        // Use shared cache so all three frameworks see the same database
        var connStr = "Data Source=SimpleCrudBench;Mode=Memory;Cache=Shared";
        _pengdowsContext = new DatabaseContext(connStr, SqliteFactory.Instance, _typeMap);
        _productHelper = new TableGateway<Product, int>(_pengdowsContext);

        // Setup Dapper with SQLite
        _dapperConnection = new SqliteConnection(connStr);
        _dapperConnection.Open();

        // Setup EF with SQLite
        var efOptions = new DbContextOptionsBuilder<EfProductContext>()
            .UseSqlite(connStr)
            .Options;
        _efContext = new EfProductContext(efOptions);

        // Create schema in all three
        CreateSchema();

        // Prepare test data
        _testProduct = new Product
        {
            Name = "Test Product",
            Price = 99.99m,
            Stock = 100,
            IsActive = true
        };

        _testProducts = Enumerable.Range(1, BatchSize)
            .Select(i => new Product
            {
                Name = $"Product {i}",
                Price = 10.00m + i,
                Stock = i * 10,
                IsActive = i % 2 == 0
            })
            .ToList();
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
        var createTableSql = @"
            CREATE TABLE products (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                price REAL NOT NULL,
                stock INTEGER NOT NULL,
                is_active INTEGER NOT NULL
            )";

        // pengdows.crud
        using (var container = _pengdowsContext.CreateSqlContainer(createTableSql))
        {
            container.ExecuteNonQueryAsync().AsTask().Wait();
        }

        // Dapper uses same connection, no need to recreate

        // EF
        _efContext.Database.EnsureCreated();
    }

    // ============================================================================
    // CREATE (INSERT) BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<int> Create_Single_Pengdows()
    {
        var product = new Product
        {
            Name = "New Product",
            Price = 49.99m,
            Stock = 50,
            IsActive = true
        };

        await _productHelper.CreateAsync(product);
        return product.Id;
    }

    [Benchmark]
    public async Task<int> Create_Single_Dapper()
    {
        var sql = "INSERT INTO products (name, price, stock, is_active) VALUES (@Name, @Price, @Stock, @IsActive); SELECT last_insert_rowid()";
        var product = new Product
        {
            Name = "New Product",
            Price = 49.99m,
            Stock = 50,
            IsActive = true
        };

        var id = await _dapperConnection.ExecuteScalarAsync<int>(sql, new
        {
            Name = product.Name,
            Price = product.Price,
            Stock = product.Stock,
            IsActive = product.IsActive ? 1 : 0
        });

        return id;
    }

    [Benchmark]
    public async Task<int> Create_Single_EntityFramework()
    {
        var product = new EfProduct
        {
            Name = "New Product",
            Price = 49.99m,
            Stock = 50,
            IsActive = true
        };

        _efContext.Products.Add(product);
        await _efContext.SaveChangesAsync();

        var id = product.Id;

        // Cleanup to prevent tracking issues
        _efContext.Entry(product).State = EntityState.Detached;

        return id;
    }

    // ============================================================================
    // READ (SELECT) SINGLE BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<Product?> Read_Single_Pengdows()
    {
        return await _productHelper.RetrieveOneAsync(1);
    }

    [Benchmark]
    public async Task<Product?> Read_Single_Dapper()
    {
        var sql = "SELECT id, name, price, stock, is_active FROM products WHERE id = @Id";
        var row = await _dapperConnection.QueryFirstOrDefaultAsync<DapperProduct>(sql, new { Id = 1 });
        return row?.ToProduct();
    }

    [Benchmark]
    public async Task<EfProduct?> Read_Single_EntityFramework()
    {
        return await _efContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == 1);
    }

    // ============================================================================
    // READ (SELECT) LIST BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<List<Product>> Read_List_Pengdows()
    {
        using var container = _pengdowsContext.CreateSqlContainer("SELECT id, name, price, stock, is_active FROM products WHERE is_active = ");
        container.Query.Append(container.MakeParameterName("isActive"));
        container.AddParameterWithValue("isActive", DbType.Boolean, true);
        return await _productHelper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<Product>> Read_List_Dapper()
    {
        var sql = "SELECT id, name, price, stock, is_active FROM products WHERE is_active = @IsActive";
        var rows = await _dapperConnection.QueryAsync<DapperProduct>(sql, new { IsActive = 1 });
        return rows.Select(r => r.ToProduct()).ToList();
    }

    [Benchmark]
    public async Task<List<EfProduct>> Read_List_EntityFramework()
    {
        return await _efContext.Products.AsNoTracking().Where(p => p.IsActive).ToListAsync();
    }

    // ============================================================================
    // UPDATE BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<int> Update_Single_Pengdows()
    {
        var product = await _productHelper.RetrieveOneAsync(1);
        if (product == null) return 0;

        product.Price = 59.99m;
        product.Stock = 75;

        return await _productHelper.UpdateAsync(product);
    }

    [Benchmark]
    public async Task<int> Update_Single_Dapper()
    {
        var sql = "UPDATE products SET price = @Price, stock = @Stock WHERE id = @Id";
        return await _dapperConnection.ExecuteAsync(sql, new
        {
            Id = 1,
            Price = 59.99m,
            Stock = 75
        });
    }

    [Benchmark]
    public async Task<int> Update_Single_EntityFramework()
    {
        var product = await _efContext.Products.FindAsync(1);
        if (product == null) return 0;

        product.Price = 59.99m;
        product.Stock = 75;

        return await _efContext.SaveChangesAsync();
    }

    // ============================================================================
    // DELETE BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<int> Delete_Single_Pengdows()
    {
        // Insert a product to delete
        var product = new Product { Name = "To Delete", Price = 1.00m, Stock = 1, IsActive = false };
        await _productHelper.CreateAsync(product);

        return await _productHelper.DeleteAsync(product.Id);
    }

    [Benchmark]
    public async Task<int> Delete_Single_Dapper()
    {
        // Insert a product to delete
        var sql = "INSERT INTO products (name, price, stock, is_active) VALUES (@Name, @Price, @Stock, @IsActive); SELECT last_insert_rowid()";
        var id = await _dapperConnection.ExecuteScalarAsync<int>(sql, new
        {
            Name = "To Delete",
            Price = 1.00m,
            Stock = 1,
            IsActive = 0
        });

        var deleteSql = "DELETE FROM products WHERE id = @Id";
        return await _dapperConnection.ExecuteAsync(deleteSql, new { Id = id });
    }

    [Benchmark]
    public async Task<int> Delete_Single_EntityFramework()
    {
        // Insert a product to delete
        var product = new EfProduct { Name = "To Delete", Price = 1.00m, Stock = 1, IsActive = false };
        _efContext.Products.Add(product);
        await _efContext.SaveChangesAsync();

        _efContext.Products.Remove(product);
        return await _efContext.SaveChangesAsync();
    }

    // ============================================================================
    // BATCH OPERATIONS
    // ============================================================================

    [Benchmark]
    public async Task<int> Create_Batch_Pengdows()
    {
        var count = 0;
        foreach (var product in _testProducts.Take(10))
        {
            var p = new Product
            {
                Name = product.Name,
                Price = product.Price,
                Stock = product.Stock,
                IsActive = product.IsActive
            };
            await _productHelper.CreateAsync(p);
            count++;
        }
        return count;
    }

    [Benchmark]
    public async Task<int> Create_Batch_Dapper()
    {
        var sql = "INSERT INTO products (name, price, stock, is_active) VALUES (@Name, @Price, @Stock, @IsActive)";
        var count = 0;
        foreach (var product in _testProducts.Take(10))
        {
            await _dapperConnection.ExecuteAsync(sql, new
            {
                Name = product.Name,
                Price = product.Price,
                Stock = product.Stock,
                IsActive = product.IsActive ? 1 : 0
            });
            count++;
        }
        return count;
    }

    [Benchmark]
    public async Task<int> Create_Batch_EntityFramework()
    {
        var products = _testProducts.Take(10).Select(p => new EfProduct
        {
            Name = p.Name,
            Price = p.Price,
            Stock = p.Stock,
            IsActive = p.IsActive
        }).ToList();

        _efContext.Products.AddRange(products);
        var count = await _efContext.SaveChangesAsync();

        // Detach to prevent tracking issues
        foreach (var p in products)
        {
            _efContext.Entry(p).State = EntityState.Detached;
        }

        return count;
    }

    public void Dispose()
    {
        Cleanup();
    }

    // ============================================================================
    // ENTITY CLASSES
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

    // Dapper mapping class (column names don't match properties)
    public class DapperProduct
    {
        public int id { get; set; }
        public string name { get; set; } = string.Empty;
        public decimal price { get; set; }
        public int stock { get; set; }
        public int is_active { get; set; }

        public Product ToProduct() => new()
        {
            Id = id,
            Name = name,
            Price = price,
            Stock = stock,
            IsActive = is_active == 1
        };
    }

    // EF entity
    public class EfProduct
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public bool IsActive { get; set; }
    }

    // EF DbContext
    public class EfProductContext : DbContext
    {
        public EfProductContext(DbContextOptions<EfProductContext> options) : base(options)
        {
        }

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
                entity.Property(e => e.Price).HasColumnType("REAL");
                entity.Property(e => e.Stock).HasColumnName("stock");
                entity.Property(e => e.IsActive).HasColumnName("is_active");
            });
        }
    }
}
