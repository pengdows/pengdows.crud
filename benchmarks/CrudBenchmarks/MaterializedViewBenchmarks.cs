using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Demonstrates pengdows.crud's materialized view advantages over Dapper and Entity Framework.
/// PostgreSQL materialized views provide pre-computed aggregations, but both Dapper and EF
/// will fall back to expensive table scans unless explicitly told to use the materialized view.
/// pengdows.crud treats materialized views as first-class entities.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5, invocationCount: 50)]
public class MaterializedViewBenchmarks : IAsyncDisposable
{
    private IContainer? _container;
    private string _connStr = string.Empty;
    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private EntityHelper<CustomerOrderSummary, int> _summaryHelper = null!;
    private EfTestDbContext _efContext = null!;
    private NpgsqlDataSource _dapperDataSource = null!;

    private readonly List<int> _customerIds = new();
    private int _testCustomerId;

    [Params(2000, 5000)] // Different dataset sizes to show scaling impact
    public int CustomerCount;

    [Params(15)] // Orders per customer average
    public int OrdersPerCustomer;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        // Use PostgreSQL container (same as existing benchmarks)
        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithEnvironment("POSTGRES_DB", "materializedview_test")
            .WithPortBinding(0, 5432)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _container.StartAsync();

        var mappedPort = _container.GetMappedPublicPort(5432);
        _connStr =
            $"Host=localhost;Port={mappedPort};Database=materializedview_test;Username=postgres;Password=postgres;Maximum Pool Size=100";

        await WaitForReady();
        await CreateSchemaAndSeedAsync();

        // Setup pengdows.crud context
        _map = new TypeMapRegistry();
        _map.Register<CustomerOrderSummary>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _connStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsContext = new DatabaseContext(cfg, NpgsqlFactory.Instance, null, _map);
        _summaryHelper = new EntityHelper<CustomerOrderSummary, int>(_pengdowsContext);

        // Setup Entity Framework context
        var options = new DbContextOptionsBuilder<EfTestDbContext>()
            .UseNpgsql(_connStr)
            .Options;
        _efContext = new EfTestDbContext(options);

        // Setup Dapper data source
        _dapperDataSource = NpgsqlDataSource.Create(_connStr);

        // Pick test customer ID
        _testCustomerId = _customerIds[0];

        Console.WriteLine(
            $"[BENCHMARK] Testing with {CustomerCount} customers, {OrdersPerCustomer} orders/customer avg");
        Console.WriteLine($"[BENCHMARK] pengdows.crud ConnectionMode: {_pengdowsContext.ConnectionMode}");

        // Verify materialized view is created and has data
        await VerifyMaterializedViewAsync();
    }

    private async Task WaitForReady()
    {
        for (var i = 0; i < 60; i++)
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

        // Create schema
        await conn.ExecuteAsync(@"
            -- Drop existing objects
            DROP MATERIALIZED VIEW IF EXISTS customer_order_summary;
            DROP TABLE IF EXISTS orders;
            DROP TABLE IF EXISTS customers;

            -- Create tables
            CREATE TABLE customers (
                customer_id SERIAL PRIMARY KEY,
                company_name VARCHAR(100) NOT NULL,
                created_date TIMESTAMP WITH TIME ZONE DEFAULT NOW()
            );

            CREATE TABLE orders (
                order_id SERIAL PRIMARY KEY,
                customer_id INTEGER NOT NULL REFERENCES customers(customer_id),
                order_date TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                total_amount DECIMAL(18,2) NOT NULL,
                status VARCHAR(20) DEFAULT 'Active'
            );

            -- Create indexes for optimal performance
            CREATE INDEX idx_orders_customer_id_status ON orders(customer_id, status);
            ", transaction: tx);

        // Insert customers
        for (var i = 1; i <= CustomerCount; i++)
        {
            var customerId = await conn.ExecuteScalarAsync<int>(
                "INSERT INTO customers (company_name) VALUES (@name) RETURNING customer_id",
                new { name = $"Company {i:D6}" }, tx);
            _customerIds.Add(customerId);
        }

        // Insert orders (varying amounts per customer)
        var random = new Random(42); // Deterministic for benchmarking
        foreach (var customerId in _customerIds)
        {
            var orderCount = Math.Max(1, random.Next(OrdersPerCustomer / 2, OrdersPerCustomer * 2));
            for (var i = 0; i < orderCount; i++)
            {
                var amount = 100 + random.Next(1, 1000);
                await conn.ExecuteAsync(@"
                    INSERT INTO orders (customer_id, total_amount, order_date)
                    VALUES (@customerId, @amount, NOW() - (@daysAgo * INTERVAL '1 day'))",
                    new
                    {
                        customerId,
                        amount,
                        daysAgo = random.Next(1, 365)
                    }, tx);
            }
        }

        // Create materialized view (the key advantage)
        await conn.ExecuteAsync(@"
            CREATE MATERIALIZED VIEW customer_order_summary AS
            SELECT
                customer_id,
                COUNT(*) as order_count,
                SUM(total_amount) as total_amount,
                AVG(total_amount) as avg_order_amount,
                MAX(order_date) as last_order_date
            FROM orders
            WHERE status = 'Active'
            GROUP BY customer_id;

            -- Create unique index for fast lookups
            CREATE UNIQUE INDEX idx_customer_order_summary_customer_id
            ON customer_order_summary(customer_id);
            ", transaction: tx);

        await tx.CommitAsync();

        // Update statistics
        await conn.ExecuteAsync(@"
            ANALYZE customers;
            ANALYZE orders;
            ANALYZE customer_order_summary;
        ");
    }

    private async Task VerifyMaterializedViewAsync()
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        var viewCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM customer_order_summary");
        Console.WriteLine($"[VERIFICATION] Materialized view contains {viewCount} customer summaries");

        // Verify we can query it
        var sample = await conn.QuerySingleOrDefaultAsync<CustomerOrderSummary>(
            "SELECT customer_id as CustomerId, order_count as OrderCount, total_amount as TotalAmount, avg_order_amount as AvgOrderAmount, last_order_date as LastOrderDate FROM customer_order_summary LIMIT 1");
        Console.WriteLine(
            $"[VERIFICATION] ✅ Materialized view working - sample customer {sample?.CustomerId} has {sample?.OrderCount} orders");
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_pengdowsContext is IAsyncDisposable pad)
        {
            await pad.DisposeAsync();
        }

        if (_efContext != null)
        {
            await _efContext.DisposeAsync();
        }

        if (_dapperDataSource != null)
        {
            await _dapperDataSource.DisposeAsync();
        }

        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// pengdows.crud using materialized view - treats view as first-class entity
    /// </summary>
    [Benchmark]
    public async Task<CustomerOrderSummary?> GetCustomerSummary_pengdows_MaterializedView()
    {
        return await _summaryHelper.RetrieveOneAsync(_testCustomerId);
    }

    /// <summary>
    /// Dapper querying base tables - doesn't know about materialized view
    /// </summary>
    [Benchmark]
    public async Task<CustomerOrderSummary?> GetCustomerSummary_Dapper_TableScan()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<CustomerOrderSummary>(@"
            SELECT
                customer_id as CustomerId,
                COUNT(*) as OrderCount,
                SUM(total_amount) as TotalAmount,
                AVG(total_amount) as AvgOrderAmount,
                MAX(order_date) as LastOrderDate
            FROM orders
            WHERE customer_id = @customerId AND status = 'Active'
            GROUP BY customer_id",
            new { customerId = _testCustomerId });
    }

    /// <summary>
    /// Dapper with explicit materialized view usage (requires manual optimization)
    /// </summary>
    [Benchmark]
    public async Task<CustomerOrderSummary?> GetCustomerSummary_Dapper_ExplicitMaterializedView()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<CustomerOrderSummary>(@"
            SELECT
                customer_id as CustomerId,
                order_count as OrderCount,
                total_amount as TotalAmount,
                avg_order_amount as AvgOrderAmount,
                last_order_date as LastOrderDate
            FROM customer_order_summary
            WHERE customer_id = @customerId",
            new { customerId = _testCustomerId });
    }

    /// <summary>
    /// Entity Framework query that will scan base tables instead of using materialized view
    /// </summary>
    [Benchmark]
    public async Task<EfCustomerOrderSummary?> GetCustomerSummary_EntityFramework_TableScan()
    {
        return await _efContext.Orders
            .Where(o => o.CustomerId == _testCustomerId && o.Status == "Active")
            .GroupBy(o => o.CustomerId)
            .Select(g => new EfCustomerOrderSummary
            {
                CustomerId = g.Key,
                OrderCount = g.Count(),
                TotalAmount = g.Sum(o => o.TotalAmount),
                AvgOrderAmount = g.Average(o => o.TotalAmount),
                LastOrderDate = g.Max(o => o.OrderDate)
            })
            .FirstOrDefaultAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
    }

    // pengdows.crud entity for materialized view
    [Table("customer_order_summary")]
    public class CustomerOrderSummary
    {
        [Id(false)] // Not generated, comes from view
        [Column("customer_id", DbType.Int32)]
        public int CustomerId { get; set; }

        [Column("order_count", DbType.Int64)] public long OrderCount { get; set; }

        [Column("total_amount", DbType.Decimal)]
        public decimal TotalAmount { get; set; }

        [Column("avg_order_amount", DbType.Decimal)]
        public decimal AvgOrderAmount { get; set; }

        [Column("last_order_date", DbType.DateTime)]
        public DateTime LastOrderDate { get; set; }
    }

    // Entity Framework entities
    public class EfTestDbContext : DbContext
    {
        public EfTestDbContext(DbContextOptions<EfTestDbContext> options) : base(options)
        {
        }

        public DbSet<EfCustomer> Customers { get; set; }
        public DbSet<EfOrder> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfCustomer>(entity =>
            {
                entity.ToTable("customers");
                entity.HasKey(e => e.CustomerId);
                entity.Property(e => e.CustomerId).HasColumnName("customer_id");
                entity.Property(e => e.CompanyName).HasColumnName("company_name").IsRequired();
                entity.Property(e => e.CreatedDate).HasColumnName("created_date");
            });

            modelBuilder.Entity<EfOrder>(entity =>
            {
                entity.ToTable("orders");
                entity.HasKey(e => e.OrderId);
                entity.Property(e => e.OrderId).HasColumnName("order_id");
                entity.Property(e => e.CustomerId).HasColumnName("customer_id");
                entity.Property(e => e.OrderDate).HasColumnName("order_date");
                entity.Property(e => e.TotalAmount).HasColumnName("total_amount");
                entity.Property(e => e.Status).HasColumnName("status");
            });
        }
    }

    public class EfCustomer
    {
        public int CustomerId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public virtual ICollection<EfOrder> Orders { get; set; } = new List<EfOrder>();
    }

    public class EfOrder
    {
        public int OrderId { get; set; }
        public int CustomerId { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "Active";
        public virtual EfCustomer Customer { get; set; } = null!;
    }

    public class EfCustomerOrderSummary
    {
        public int CustomerId { get; set; }
        public int OrderCount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AvgOrderAmount { get; set; }
        public DateTime LastOrderDate { get; set; }
    }
}