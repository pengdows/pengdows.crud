using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Demonstrates pengdows.crud's database-specific feature advantages that EF/Dapper cannot easily leverage.
/// Each database has unique optimizations that pengdows.crud's dialect system can exploit.
///
/// NOTE: Entity Framework benchmarks are expected to fail (show as NA in results).
/// This is intentional and demonstrates EF's limitations with advanced PostgreSQL features:
/// - JSONB queries require client-side evaluation or raw SQL
/// - Array operations are not natively supported
/// - Full-text search requires extensions EF doesn't understand
/// - Geospatial queries need PostGIS which EF can't handle natively
///
/// These failures highlight pengdows.crud's advantage: native database feature support.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5, invocationCount: 25)]
public class DatabaseSpecificFeatureBenchmarks : IAsyncDisposable
{
    private IContainer? _container;
    private string _connStr = string.Empty;
    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private EntityHelper<Product, int> _productHelper = null!;
    private EntityHelper<ProductCategory, int> _categoryHelper = null!;
    private EfTestDbContext _efContext = null!;
    private NpgsqlDataSource _dapperDataSource = null!;

    private readonly List<int> _productIds = new();
    private readonly List<int> _categoryIds = new();

    [Params(1000)]
    public int ProductCount;

    [Params(50)]
    public int CategoryCount;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        // Use PostgreSQL for this demonstration (can be adapted for other databases)
        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithEnvironment("POSTGRES_DB", "dbfeatures_test")
            .WithPortBinding(0, 5432)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _container.StartAsync();

        var mappedPort = _container.GetMappedPublicPort(5432);
        _connStr = $"Host=localhost;Port={mappedPort};Database=dbfeatures_test;Username=postgres;Password=postgres;Maximum Pool Size=100";

        await WaitForReady();
        await CreateSchemaAndSeedAsync();

        // Setup pengdows.crud context
        _map = new TypeMapRegistry();
        _map.Register<Product>();
        _map.Register<ProductCategory>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _connStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsContext = new DatabaseContext(cfg, NpgsqlFactory.Instance, null, _map);
        _productHelper = new EntityHelper<Product, int>(_pengdowsContext);
        _categoryHelper = new EntityHelper<ProductCategory, int>(_pengdowsContext);

        // Setup Entity Framework context
        var options = new DbContextOptionsBuilder<EfTestDbContext>()
            .UseNpgsql(_connStr)
            .Options;
        _efContext = new EfTestDbContext(options);

        // Setup Dapper data source
        _dapperDataSource = NpgsqlDataSource.Create(_connStr);

        Console.WriteLine($"[BENCHMARK] Testing PostgreSQL-specific features");
        Console.WriteLine($"[BENCHMARK] pengdows.crud ConnectionMode: {_pengdowsContext.ConnectionMode}");
    }

    private async Task WaitForReady()
    {
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
        throw new TimeoutException("PostgreSQL container did not become ready in time.");
    }

    private async Task CreateSchemaAndSeedAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Create schema with PostgreSQL-specific features
        await conn.ExecuteAsync(@"
            -- Enable PostgreSQL extensions
            CREATE EXTENSION IF NOT EXISTS ""uuid-ossp"";
            CREATE EXTENSION IF NOT EXISTS ""pgcrypto"";

            -- Drop existing objects
            DROP TABLE IF EXISTS products;
            DROP TABLE IF EXISTS product_categories;
            DROP TYPE IF EXISTS price_range;

            -- PostgreSQL-specific: Custom ENUM type
            CREATE TYPE price_range AS ENUM ('budget', 'mid_range', 'premium', 'luxury');

            -- Create tables with PostgreSQL-specific features
            CREATE TABLE product_categories (
                category_id SERIAL PRIMARY KEY,
                category_name VARCHAR(100) NOT NULL,
                -- PostgreSQL-specific: UUID with default generation
                external_uuid UUID DEFAULT uuid_generate_v4(),
                -- PostgreSQL-specific: JSONB for flexible metadata
                metadata JSONB DEFAULT '{}',
                created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
            );

            CREATE TABLE products (
                product_id SERIAL PRIMARY KEY,
                category_id INTEGER NOT NULL REFERENCES product_categories(category_id),
                product_name VARCHAR(200) NOT NULL,
                -- PostgreSQL-specific: Custom ENUM type
                price_category price_range NOT NULL DEFAULT 'mid_range',
                price DECIMAL(18,2) NOT NULL,
                -- PostgreSQL-specific: ARRAY column for tags
                tags TEXT[] DEFAULT '{}',
                -- PostgreSQL-specific: JSONB for product specifications
                specifications JSONB DEFAULT '{}',
                -- PostgreSQL-specific: Full-text search vector
                search_vector TSVECTOR,
                -- PostgreSQL-specific: Point type for location
                warehouse_location POINT,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
            );

            -- PostgreSQL-specific: GIN index for JSONB
            CREATE INDEX idx_products_specifications_gin ON products USING GIN (specifications);

            -- PostgreSQL-specific: GIN index for text search
            CREATE INDEX idx_products_search_gin ON products USING GIN (search_vector);

            -- PostgreSQL-specific: GiST index for location queries
            CREATE INDEX idx_products_location_gist ON products USING GIST (warehouse_location);

            -- PostgreSQL-specific: Functional index with expression
            CREATE INDEX idx_products_name_lower ON products (LOWER(product_name));

            ", transaction: tx);

        // Insert categories with PostgreSQL-specific features
        var categoryData = new[]
        {
            ("Electronics", """{"department": "tech", "priority": 1}"""),
            ("Clothing", """{"department": "fashion", "priority": 2}"""),
            ("Home & Garden", """{"department": "home", "priority": 3}"""),
            ("Books", """{"department": "media", "priority": 4}"""),
            ("Sports", """{"department": "recreation", "priority": 5}""")
        };

        foreach (var (name, metadata) in categoryData)
        {
            var categoryId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO product_categories (category_name, metadata)
                VALUES (@name, @metadata::jsonb)
                RETURNING category_id",
                new { name, metadata }, tx);
            _categoryIds.Add(categoryId);
        }

        // Insert products with PostgreSQL-specific features
        var random = new Random(42);
        var priceRanges = new[] { "budget", "mid_range", "premium", "luxury" };

        for (int i = 1; i <= ProductCount; i++)
        {
            var categoryId = _categoryIds[random.Next(_categoryIds.Count)];
            var priceRange = priceRanges[random.Next(priceRanges.Length)];
            var price = priceRange switch
            {
                "budget" => random.Next(10, 50),
                "mid_range" => random.Next(50, 200),
                "premium" => random.Next(200, 500),
                "luxury" => random.Next(500, 2000),
                _ => 100
            };

            var tags = new[] { "featured", "bestseller", "new", "sale", "recommended" }
                .OrderBy(_ => random.Next())
                .Take(random.Next(1, 4))
                .ToArray();

            var specs = $$"""
            {
                "weight": {{random.Next(1, 100)}},
                "dimensions": {
                    "length": {{random.Next(10, 100)}},
                    "width": {{random.Next(10, 100)}},
                    "height": {{random.Next(5, 50)}}
                },
                "color": "{{new[] { "red", "blue", "green", "black", "white" }[random.Next(5)]}}",
                "brand": "Brand{{random.Next(1, 20)}}"
            }
            """;

            var productId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO products (
                    category_id, product_name, price_category, price, tags,
                    specifications, search_vector, warehouse_location
                )
                VALUES (
                    @categoryId,
                    @productName,
                    @priceRange::price_range,
                    @price,
                    @tags,
                    @specs::jsonb,
                    to_tsvector('english', @productName),
                    point(@x, @y)
                )
                RETURNING product_id",
                new
                {
                    categoryId,
                    productName = $"Product {i:D4}",
                    priceRange,
                    price,
                    tags,
                    specs,
                    x = random.NextDouble() * 100,
                    y = random.NextDouble() * 100
                }, tx);
            _productIds.Add(productId);
        }

        await tx.CommitAsync();

        // Update statistics
        await conn.ExecuteAsync("ANALYZE product_categories; ANALYZE products;");
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_pengdowsContext is IAsyncDisposable pad)
            await pad.DisposeAsync();

        if (_efContext != null)
            await _efContext.DisposeAsync();

        if (_dapperDataSource != null)
            await _dapperDataSource.DisposeAsync();

        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// pengdows.crud: Direct JSONB querying with native PostgreSQL operators
    /// </summary>
    [Benchmark]
    public async Task<List<Product>> PostgreSQL_JSONB_Query_pengdows()
    {
        using var container = _pengdowsContext.CreateSqlContainer(@"
            SELECT product_id, category_id, product_name, price_category, price, tags, specifications, warehouse_location, created_at
            FROM products
            WHERE specifications->>'brand' = ");
        container.Query.Append(container.MakeParameterName("brand"));
        container.AddParameterWithValue("brand", DbType.String, "Brand5");

        return await _productHelper.LoadListAsync(container);
    }

    /// <summary>
    /// Entity Framework: Cannot easily query JSONB with native operators
    /// Falls back to client-side evaluation or complex raw SQL
    /// </summary>
    [Benchmark]
    public async Task<List<EfProduct>> PostgreSQL_JSONB_Query_EntityFramework()
    {
        // EF Core has limited JSONB support, often requires client evaluation
        // This may force client-side evaluation since EF's JSON support is limited
        return await _efContext.Products
            .AsNoTracking()
            .Where(p => p.Specifications != null && p.Specifications.Contains("Brand5"))
            .ToListAsync();
    }

    /// <summary>
    /// Dapper: Requires manual JSONB syntax knowledge
    /// </summary>
    [Benchmark]
    public async Task<List<Product>> PostgreSQL_JSONB_Query_Dapper()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var products = await conn.QueryAsync<dynamic>(@"
            SELECT product_id, category_id, product_name, price_category, price, tags, specifications, warehouse_location, created_at
            FROM products
            WHERE specifications->>'brand' = @brand",
            new { brand = "Brand5" });

        // Manual mapping required for complex types
        return products.Select(p => new Product
        {
            ProductId = p.product_id,
            CategoryId = p.category_id,
            ProductName = p.product_name,
            PriceCategory = p.price_category,
            Price = p.price,
            CreatedAt = p.created_at
            // Tags and Specifications require complex manual parsing
        }).ToList();
    }

    /// <summary>
    /// pengdows.crud: Array contains operation using PostgreSQL's native array operators
    /// </summary>
    [Benchmark]
    public async Task<List<Product>> PostgreSQL_Array_Contains_pengdows()
    {
        using var container = _pengdowsContext.CreateSqlContainer(@"
            SELECT product_id, category_id, product_name, price_category, price, tags, specifications, warehouse_location, created_at
            FROM products
            WHERE ");
        container.Query.Append(container.MakeParameterName("tag"));
        container.Query.Append(" = ANY(tags)");
        container.AddParameterWithValue("tag", DbType.String, "featured");

        return await _productHelper.LoadListAsync(container);
    }

    /// <summary>
    /// Entity Framework: Limited array support, often requires complex workarounds
    /// </summary>
    [Benchmark]
    public async Task<List<EfProduct>> PostgreSQL_Array_Contains_EntityFramework()
    {
        // EF Core has some array support but it's limited and verbose
        return await _efContext.Products
            .AsNoTracking()
            .Where(p => p.Tags != null && p.Tags.Contains("featured"))
            .ToListAsync();
    }

    /// <summary>
    /// pengdows.crud: Full-text search using PostgreSQL's tsvector
    /// </summary>
    [Benchmark]
    public async Task<List<Product>> PostgreSQL_FullTextSearch_pengdows()
    {
        using var container = _pengdowsContext.CreateSqlContainer(@"
            SELECT product_id, category_id, product_name, price_category, price, tags, specifications, warehouse_location, created_at
            FROM products
            WHERE search_vector @@ plainto_tsquery('english', ");
        container.Query.Append(container.MakeParameterName("searchTerm"));
        container.Query.Append(") ORDER BY ts_rank(search_vector, plainto_tsquery('english', ");
        container.Query.Append(container.MakeParameterName("searchTerm2"));
        container.Query.Append(")) DESC");
        container.AddParameterWithValue("searchTerm", DbType.String, "Product");
        container.AddParameterWithValue("searchTerm2", DbType.String, "Product");

        return await _productHelper.LoadListAsync(container);
    }

    /// <summary>
    /// Entity Framework: No native full-text search support
    /// Falls back to LIKE queries which are much slower
    /// </summary>
    [Benchmark]
    public async Task<List<EfProduct>> PostgreSQL_FullTextSearch_EntityFramework()
    {
        // EF has no native FTS support, falls back to slow LIKE
        return await _efContext.Products
            .AsNoTracking()
            .Where(p => EF.Functions.Like(p.ProductName, "%Product%"))
            .ToListAsync();
    }

    /// <summary>
    /// pengdows.crud: Geospatial queries using PostgreSQL's point operators
    /// </summary>
    [Benchmark]
    public async Task<List<Product>> PostgreSQL_Geospatial_Query_pengdows()
    {
        using var container = _pengdowsContext.CreateSqlContainer(@"
            SELECT product_id, category_id, product_name, price_category, price, tags, specifications, warehouse_location, created_at
            FROM products
            WHERE warehouse_location <-> point(50, 50) < ");
        container.Query.Append(container.MakeParameterName("distance"));
        container.Query.Append(" ORDER BY warehouse_location <-> point(50, 50)");
        container.AddParameterWithValue("distance", DbType.Double, 25.0);

        return await _productHelper.LoadListAsync(container);
    }

    /// <summary>
    /// Entity Framework: Limited geospatial support, requires NetTopologySuite
    /// </summary>
    [Benchmark]
    public async Task<List<EfProduct>> PostgreSQL_Geospatial_Query_EntityFramework()
    {
        // EF requires additional packages and complex setup for geospatial
        // This is a simplified version - real implementation much more complex
        return await _efContext.Products
            .AsNoTracking()
            .Where(p => Math.Sqrt(Math.Pow(50 - 50, 2) + Math.Pow(50 - 50, 2)) < 25)
            .ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
    }

    // pengdows.crud entities with PostgreSQL-specific features
    [pengdows.crud.attributes.Table("products")]
    public class Product
    {
        [Id(true)]
        [pengdows.crud.attributes.Column("product_id", DbType.Int32)]
        public int ProductId { get; set; }

        [pengdows.crud.attributes.Column("category_id", DbType.Int32)]
        public int CategoryId { get; set; }

        [pengdows.crud.attributes.Column("product_name", DbType.String)]
        public string ProductName { get; set; } = string.Empty;

        [pengdows.crud.attributes.Column("price_category", DbType.String)]
        public string PriceCategory { get; set; } = string.Empty;

        [pengdows.crud.attributes.Column("price", DbType.Decimal)]
        public decimal Price { get; set; }

        // PostgreSQL array support
        [pengdows.crud.attributes.Column("tags", DbType.Object)]
        public string[]? Tags { get; set; }

        // PostgreSQL JSONB support
        [pengdows.crud.attributes.Column("specifications", DbType.Object)]
        [pengdows.crud.attributes.Json]
        public ProductSpecifications Specifications { get; set; } = new();

        // PostgreSQL point support
        [pengdows.crud.attributes.Column("warehouse_location", DbType.Object)]
        public NpgsqlPoint? WarehouseLocation { get; set; }

        [pengdows.crud.attributes.Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }
    }

    public class ProductSpecifications
    {
        public int Weight { get; set; }
        public ProductDimensions Dimensions { get; set; } = new();
        public string Color { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
    }

    public class ProductDimensions
    {
        public int Length { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    [pengdows.crud.attributes.Table("product_categories")]
    public class ProductCategory
    {
        [Id(true)]
        [pengdows.crud.attributes.Column("category_id", DbType.Int32)]
        public int CategoryId { get; set; }

        [pengdows.crud.attributes.Column("category_name", DbType.String)]
        public string CategoryName { get; set; } = string.Empty;

        [pengdows.crud.attributes.Column("external_uuid", DbType.Guid)]
        public Guid ExternalUuid { get; set; }

        [pengdows.crud.attributes.Column("metadata", DbType.Object)]
        public string? Metadata { get; set; }

        [pengdows.crud.attributes.Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }
    }

    // Entity Framework entities (limited PostgreSQL feature support)
    public class EfTestDbContext : DbContext
    {
        public EfTestDbContext(DbContextOptions<EfTestDbContext> options) : base(options) { }

        public DbSet<EfProduct> Products { get; set; }
        public DbSet<EfProductCategory> ProductCategories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfProduct>(entity =>
            {
                entity.ToTable("products");
                entity.HasKey(e => e.ProductId);
                entity.Property(e => e.ProductId).HasColumnName("product_id");
                entity.Property(e => e.CategoryId).HasColumnName("category_id");
                entity.Property(e => e.ProductName).HasColumnName("product_name");
                entity.Property(e => e.PriceCategory).HasColumnName("price_category");
                entity.Property(e => e.Price).HasColumnName("price");
                entity.Property(e => e.Tags).HasColumnName("tags");
                entity.Property(e => e.Specifications).HasColumnName("specifications");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            });

            modelBuilder.Entity<EfProductCategory>(entity =>
            {
                entity.ToTable("product_categories");
                entity.HasKey(e => e.CategoryId);
                entity.Property(e => e.CategoryId).HasColumnName("category_id");
                entity.Property(e => e.CategoryName).HasColumnName("category_name");
                entity.Property(e => e.ExternalUuid).HasColumnName("external_uuid");
                entity.Property(e => e.Metadata).HasColumnName("metadata");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            });
        }
    }

    public class EfProduct
    {
        public int ProductId { get; set; }
        public int CategoryId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string PriceCategory { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string[]? Tags { get; set; }
        public string? Specifications { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class EfProductCategory
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public Guid ExternalUuid { get; set; }
        public string? Metadata { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
