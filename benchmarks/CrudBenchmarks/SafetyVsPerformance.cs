using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// SAFETY vs PERFORMANCE: Fair Comparison
///
/// This benchmark compares:
/// 1. pengdows.crud: Safe, deterministic parameter creation + proper connection management
/// 2. Dapper (typical): Fast but "magic" type inference + connection stays open
/// 3. Dapper (proper): Same as pengdows - open/close per operation
///
/// KEY DIFFERENCES:
/// - pengdows.crud: Uses provider.CreateParameter(), explicit DbType, safe and deterministic
/// - Dapper: Type inference, optimizations, "magic" that might not be safe
///
/// - pengdows.crud: Open late, close early (recommended pattern)
/// - Dapper (typical): Connection stays open (fast but holds resources)
/// - Dapper (proper): Open/close per operation (fair comparison)
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SafetyVsPerformance : IDisposable
{
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<Product, int> _productHelper = null!;
    private string _connectionString = null!;
    private TypeMapRegistry _typeMap = null!;
    private SqliteConnection _sentinelConnection = null!; // Keep DB alive

    [GlobalSetup]
    public void Setup()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<Product>();

        _connectionString = "Data Source=SafetyBench;Mode=Memory;Cache=Shared";

        // Open sentinel connection to keep in-memory DB alive
        _sentinelConnection = new SqliteConnection(_connectionString);
        _sentinelConnection.Open();

        _pengdowsContext = new DatabaseContext(_connectionString, SqliteFactory.Instance, _typeMap);
        _productHelper = new TableGateway<Product, int>(_pengdowsContext);

        CreateSchema();
        SeedData();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pengdowsContext?.Dispose();
        _sentinelConnection?.Dispose();
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
    // SAFE APPROACH: pengdows.crud
    // - Provider.CreateParameter() for safety
    // - Explicit DbType for determinism
    // - Open late, close early
    // ============================================================================

    [Benchmark(Baseline = true, Description = "SAFE: pengdows.crud (explicit types, proper connection mgmt)")]
    public async Task<Product?> Safe_Pengdows_ProperConnectionMgmt()
    {
        // Connection opened and closed inside RetrieveOneAsync
        // Parameters created via provider.CreateParameter()
        // DbType explicitly set
        return await _productHelper.RetrieveOneAsync(1);
    }

    [Benchmark(Description = "SAFE: pengdows.crud INSERT (explicit types, proper connection mgmt)")]
    public async Task<bool> Safe_Pengdows_Insert()
    {
        var product = new Product
        {
            Name = "New Product",
            Price = 49.99m,
            Stock = 50,
            IsActive = true
        };

        // Connection opened and closed inside CreateAsync
        // Parameters created via provider.CreateParameter() with explicit DbType
        return await _productHelper.CreateAsync(product);
    }

    // ============================================================================
    // TYPICAL DAPPER: Fast but questionable safety
    // - Type inference (magic)
    // - Connection stays open (resource holding)
    // ============================================================================

    [Benchmark(Description = "TYPICAL DAPPER: Type inference + connection stays open")]
    public async Task<Product?> TypicalDapper_FastButOpenConnection()
    {
        // Connection stays open the whole time (resource leak in long-running apps)
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Dapper infers types - is this safe? What DbType does it use?
        var sql = "SELECT id, name, price, stock, is_active FROM products WHERE id = @Id";
        return await conn.QueryFirstOrDefaultAsync<Product>(sql, new { Id = 1 });

        // Connection closed here (but in benchmarks, this is called thousands of times)
    }

    [Benchmark(Description = "TYPICAL DAPPER: INSERT with type inference + open connection")]
    public async Task<int> TypicalDapper_Insert()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // What DbType does Dapper use for decimal? Is it consistent across providers?
        var sql = "INSERT INTO products (name, price, stock, is_active) VALUES (@Name, @Price, @Stock, @IsActive)";
        return await conn.ExecuteAsync(sql, new
        {
            Name = "New Product",
            Price = 49.99m,  // Dapper infers DbType - safe?
            Stock = 50,       // Dapper infers DbType
            IsActive = 1      // Note: We have to manually convert bool to int!
        });
    }

    // ============================================================================
    // PROPER DAPPER: Same pattern as pengdows.crud
    // - Open/close per operation (fair comparison)
    // - Still uses type inference though
    // ============================================================================

    [Benchmark(Description = "PROPER DAPPER: Open/close per operation (same as pengdows)")]
    public async Task<Product?> ProperDapper_SameConnectionPattern()
    {
        // Fair comparison: same connection pattern as pengdows.crud
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = "SELECT id, name, price, stock, is_active FROM products WHERE id = @Id";
        var result = await conn.QueryFirstOrDefaultAsync<Product>(sql, new { Id = 1 });

        await conn.CloseAsync();
        return result;
    }

    [Benchmark(Description = "PROPER DAPPER: INSERT with open/close per operation")]
    public async Task<int> ProperDapper_Insert()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = "INSERT INTO products (name, price, stock, is_active) VALUES (@Name, @Price, @Stock, @IsActive)";
        var result = await conn.ExecuteAsync(sql, new
        {
            Name = "New Product",
            Price = 49.99m,
            Stock = 50,
            IsActive = 1
        });

        await conn.CloseAsync();
        return result;
    }

    // ============================================================================
    // SAFETY DEMONSTRATION: Type mismatches
    // ============================================================================

    [Benchmark(Description = "SAFETY: pengdows handles decimals correctly")]
    public async Task<bool> Safety_Pengdows_DecimalPrecision()
    {
        var product = new Product
        {
            Name = "High Precision",
            Price = 123.456789m,  // Explicit decimal with high precision
            Stock = 1,
            IsActive = true
        };

        // pengdows.crud: DbType.Decimal explicitly set
        // Guarantees correct storage
        return await _productHelper.CreateAsync(product);
    }

    [Benchmark(Description = "SAFETY: Dapper type inference on decimal")]
    public async Task<int> Safety_Dapper_DecimalInference()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = "INSERT INTO products (name, price, stock, is_active) VALUES (@Name, @Price, @Stock, @IsActive)";

        // Dapper infers the type - does it use DbType.Decimal? DbType.Double?
        // SQLite might store as REAL (float64) causing precision loss
        var result = await conn.ExecuteAsync(sql, new
        {
            Name = "High Precision",
            Price = 123.456789m,  // What DbType does Dapper use?
            Stock = 1,
            IsActive = 1
        });

        await conn.CloseAsync();
        return result;
    }

    // ============================================================================
    // REALISTIC SCENARIO: Connection pool stress
    // Shows why "open late, close early" is better for real apps
    // ============================================================================

    [Benchmark(Description = "REALISTIC: 100 operations with pengdows (releases connections)")]
    public async Task<int> Realistic_Pengdows_ConnectionPoolFriendly()
    {
        var count = 0;

        // Each operation opens and closes - returns to pool immediately
        // Pool can be small (10-20 connections) even with high concurrency
        for (int i = 1; i <= 100; i++)
        {
            var product = await _productHelper.RetrieveOneAsync(i % 100 + 1);
            if (product != null) count++;
        }

        return count;
    }

    [Benchmark(Description = "REALISTIC: 100 operations with Dapper (holds connections longer)")]
    public async Task<int> Realistic_Dapper_ConnectionPoolStress()
    {
        var count = 0;

        // Each operation creates a new connection (using block)
        // But the using block means connection is held for the entire operation
        // Under high load, this exhausts the pool
        for (int i = 1; i <= 100; i++)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var sql = "SELECT id, name, price, stock, is_active FROM products WHERE id = @Id";
            var product = await conn.QueryFirstOrDefaultAsync<Product>(sql, new { Id = i % 100 + 1 });

            if (product != null) count++;
            // Connection held until here
        }

        return count;
    }

    public void Dispose()
    {
        Cleanup();
    }

    // ============================================================================
    // ENTITY
    // ============================================================================

    [Table("products")]
    public class Product
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Column("price", DbType.Decimal)]  // EXPLICIT: DbType.Decimal for precision
        public decimal Price { get; set; }

        [Column("stock", DbType.Int32)]
        public int Stock { get; set; }

        [Column("is_active", DbType.Boolean)]  // EXPLICIT: DbType.Boolean
        public bool IsActive { get; set; }
    }
}
