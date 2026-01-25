using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Demonstrates pengdows.crud's indexed view advantages over Entity Framework.
/// EF's session settings (ARITHABORT OFF) prevent indexed view usage,
/// while pengdows.crud preserves database optimizations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5, invocationCount: 50)]
public class IndexedViewBenchmarks : IAsyncDisposable
{
    private IContainer? _sqlServerContainer;
    private string _connStr = string.Empty;
    private const string Password = "YourStrong@Passw0rd";
    private const string Database = "IndexedViewBenchmark";

    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private EntityHelper<CustomerOrderSummary, int> _summaryHelper = null!;
    private EfTestDbContext _efContext = null!;
    private SqlConnection _directSqlConnection = null!;

    private readonly List<int> _customerIds = new();
    private int _testCustomerId;

    [Params(1000, 5000)] // Different dataset sizes to show scaling impact
    public int CustomerCount;

    [Params(10)] // Orders per customer average
    public int OrdersPerCustomer;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        // Start SQL Server container using Testcontainers
        _sqlServerContainer = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SA_PASSWORD", Password)
            .WithEnvironment("MSSQL_PID", "Developer")
            .WithPortBinding(1433, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
            .Build();

        Console.WriteLine("[BENCHMARK] Starting SQL Server container...");
        await _sqlServerContainer.StartAsync();

        var hostPort = _sqlServerContainer.GetMappedPublicPort(1433);
        _connStr =
            $"Server=localhost,{hostPort};Database={Database};User Id=sa;Password={Password};TrustServerCertificate=true;Connection Timeout=30;";

        Console.WriteLine($"[BENCHMARK] SQL Server container started on port {hostPort}");

        // Wait for SQL Server to be ready with retry logic
        await WaitForSqlServerAsync();

        await CreateDatabaseAndSchemaAsync();
        await SeedDataAsync();

        // Setup pengdows.crud context
        _map = new TypeMapRegistry();
        _map.Register<CustomerOrderSummary>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _connStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsContext = new DatabaseContext(cfg, SqlClientFactory.Instance, null, _map);
        _summaryHelper = new EntityHelper<CustomerOrderSummary, int>(_pengdowsContext);

        // Setup Entity Framework context
        var options = new DbContextOptionsBuilder<EfTestDbContext>()
            .UseSqlServer(_connStr)
            .Options;
        _efContext = new EfTestDbContext(options);

        // Setup direct SQL connection for comparison
        _directSqlConnection = new SqlConnection(_connStr);
        await _directSqlConnection.OpenAsync();

        // Pick test customer IDs
        _testCustomerId = _customerIds[0];

        Console.WriteLine(
            $"[BENCHMARK] Testing with {CustomerCount} customers, {OrdersPerCustomer} orders/customer avg");
        Console.WriteLine($"[BENCHMARK] pengdows.crud ConnectionMode: {_pengdowsContext.ConnectionMode}");

        // Verify indexed view is created and working
        await VerifyIndexedViewAsync();
    }

    private async Task WaitForSqlServerAsync()
    {
        var masterConnStr = _connStr.Replace($"Database={Database}", "Database=master");
        Console.WriteLine("[BENCHMARK] Waiting for SQL Server to be ready...");

        for (var i = 0; i < 120; i++) // 2 minutes max
        {
            try
            {
                await using var conn = new SqlConnection(masterConnStr);
                await conn.OpenAsync();
                var result = await conn.ExecuteScalarAsync<int>("SELECT 1");
                if (result == 1)
                {
                    Console.WriteLine($"[BENCHMARK] SQL Server ready after {i + 1} attempts");
                    return;
                }
            }
            catch (Exception ex)
            {
                if (i % 10 == 0 && i > 0)
                {
                    Console.WriteLine($"[BENCHMARK] Waiting for SQL Server... attempt {i + 1}/120: {ex.Message}");
                }

                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("SQL Server did not become ready within 2 minutes");
    }

    private async Task CreateDatabaseAndSchemaAsync()
    {
        // Create database if it doesn't exist
        var masterConnStr = _connStr.Replace($"Database={Database}", "Database=master");
        await using var masterConn = new SqlConnection(masterConnStr);
        await masterConn.OpenAsync();

        await masterConn.ExecuteAsync($@"
            IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{Database}')
            BEGIN
                CREATE DATABASE [{Database}];
            END");

        // Create schema and indexed view - must be split into separate batches
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();

        // Drop existing objects
        await conn.ExecuteAsync(@"
            IF OBJECT_ID('dbo.vw_CustomerOrderSummary', 'V') IS NOT NULL
                DROP VIEW dbo.vw_CustomerOrderSummary;

            IF OBJECT_ID('dbo.Orders', 'U') IS NOT NULL
                DROP TABLE dbo.Orders;

            IF OBJECT_ID('dbo.Customers', 'U') IS NOT NULL
                DROP TABLE dbo.Customers;");

        // Create tables
        await conn.ExecuteAsync(@"
            CREATE TABLE dbo.Customers (
                customer_id INT IDENTITY(1,1) PRIMARY KEY,
                company_name NVARCHAR(100) NOT NULL,
                created_date DATETIME2 DEFAULT GETUTCDATE()
            );

            CREATE TABLE dbo.Orders (
                order_id INT IDENTITY(1,1) PRIMARY KEY,
                customer_id INT NOT NULL REFERENCES dbo.Customers(customer_id),
                order_date DATETIME2 DEFAULT GETUTCDATE(),
                total_amount DECIMAL(18,2) NOT NULL,
                status NVARCHAR(20) DEFAULT 'Active'
            );");

        // Create indexed view (must be in its own batch)
        // Note: AVG() not allowed in indexed views, use SUM/COUNT_BIG instead
        await conn.ExecuteAsync(@"
            CREATE VIEW dbo.vw_CustomerOrderSummary WITH SCHEMABINDING AS
            SELECT
                customer_id,
                COUNT_BIG(*) as order_count,
                SUM(total_amount) as total_amount,
                SUM(total_amount) as sum_order_amount,
                COUNT_BIG(*) as count_for_avg
            FROM dbo.Orders
            WHERE status = 'Active'
            GROUP BY customer_id;");

        // Create indexes
        await conn.ExecuteAsync(@"
            CREATE UNIQUE CLUSTERED INDEX IX_CustomerOrderSummary_CustomerID
            ON dbo.vw_CustomerOrderSummary(customer_id);

            CREATE INDEX IX_Orders_CustomerID_Status ON dbo.Orders(customer_id, status);");
    }

    private async Task SeedDataAsync()
    {
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Insert customers
        for (var i = 1; i <= CustomerCount; i++)
        {
            var customerId = await conn.ExecuteScalarAsync<int>(
                "INSERT INTO dbo.Customers (company_name) OUTPUT INSERTED.customer_id VALUES (@name)",
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
                    INSERT INTO dbo.Orders (customer_id, total_amount, order_date)
                    VALUES (@customerId, @amount, DATEADD(day, -@daysAgo, GETUTCDATE()))",
                    new
                    {
                        customerId,
                        amount,
                        daysAgo = random.Next(1, 365)
                    }, tx);
            }
        }

        await tx.CommitAsync();

        // Update statistics to ensure optimal query plans
        await conn.ExecuteAsync(@"
            UPDATE STATISTICS dbo.Customers;
            UPDATE STATISTICS dbo.Orders;
            UPDATE STATISTICS dbo.vw_CustomerOrderSummary;
        ");
    }

    private async Task VerifyIndexedViewAsync()
    {
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();

        // Verify the indexed view exists and has data
        var viewCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.vw_CustomerOrderSummary");
        Console.WriteLine($"[VERIFICATION] Indexed view contains {viewCount} customer summaries");

        // Check if indexed view will be used (requires ARITHABORT ON)
        await conn.ExecuteAsync("SET ARITHABORT ON");
        string? plan = null;
        try
        {
            await conn.ExecuteAsync("SET SHOWPLAN_TEXT ON");
            plan = await conn.QuerySingleOrDefaultAsync<string>(
                "SELECT customer_id, order_count FROM dbo.vw_CustomerOrderSummary WHERE customer_id = 1");
        }
        finally
        {
            await conn.ExecuteAsync("SET SHOWPLAN_TEXT OFF");
        }

        if (plan?.Contains("vw_CustomerOrderSummary") == true)
        {
            Console.WriteLine("[VERIFICATION] ✅ Indexed view will be used with proper session settings");
        }
        else
        {
            Console.WriteLine("[VERIFICATION] ⚠️ Indexed view may not be optimal");
        }
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

        if (_directSqlConnection != null)
        {
            await _directSqlConnection.DisposeAsync();
        }
    }

    /// <summary>
    /// pengdows.crud using indexed view - preserves database optimizations
    /// </summary>
    [Benchmark]
    public async Task<CustomerOrderSummary?> GetCustomerSummary_pengdows_IndexedView()
    {
        return await _summaryHelper.RetrieveOneAsync(_testCustomerId);
    }

    /// <summary>
    /// Entity Framework query that should use indexed view but can't due to ARITHABORT OFF
    /// </summary>
    [Benchmark]
    public async Task<EfCustomerOrderSummary?> GetCustomerSummary_EntityFramework_Aggregation()
    {
        return await _efContext.Orders
            .Where(o => o.CustomerId == _testCustomerId && o.Status == "Active")
            .GroupBy(o => o.CustomerId)
            .Select(g => new EfCustomerOrderSummary
            {
                CustomerId = g.Key,
                OrderCount = g.Count(),
                TotalAmount = g.Sum(o => o.TotalAmount),
                AvgOrderAmount = g.Average(o => o.TotalAmount)
            })
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Entity Framework with manual ARITHABORT ON (workaround, but brittle)
    /// </summary>
    [Benchmark]
    public async Task<EfCustomerOrderSummary?> GetCustomerSummary_EntityFramework_WithWorkaround()
    {
        // Manual workaround - set ARITHABORT ON to enable indexed view
        await _efContext.Database.ExecuteSqlRawAsync("SET ARITHABORT ON");

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

    /// <summary>
    /// Direct SQL with proper session settings (baseline performance)
    /// </summary>
    [Benchmark]
    public async Task<CustomerOrderSummary?> GetCustomerSummary_DirectSQL_IndexedView()
    {
        return await _directSqlConnection.QuerySingleOrDefaultAsync<CustomerOrderSummary>(@"
            SET ARITHABORT ON;
            SELECT customer_id as CustomerId, order_count as OrderCount,
                   total_amount as TotalAmount, sum_order_amount as SumOrderAmount,
                   count_for_avg as CountForAvg
            FROM dbo.vw_CustomerOrderSummary
            WHERE customer_id = @customerId",
            new { customerId = _testCustomerId });
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();

        // Stop and dispose SQL Server container
        if (_sqlServerContainer != null)
        {
            Console.WriteLine("[BENCHMARK] Stopping SQL Server container...");
            await _sqlServerContainer.DisposeAsync();
        }
    }

    // pengdows.crud entity for indexed view
    [pengdows.crud.attributes.Table("vw_CustomerOrderSummary", "dbo")]
    public class CustomerOrderSummary
    {
        [Id(false)] // Not generated, comes from view
        [pengdows.crud.attributes.Column("customer_id", DbType.Int32)]
        public int CustomerId { get; set; }

        [pengdows.crud.attributes.Column("order_count", DbType.Int64)]
        public long OrderCount { get; set; }

        [pengdows.crud.attributes.Column("total_amount", DbType.Decimal)]
        public decimal TotalAmount { get; set; }

        [pengdows.crud.attributes.Column("sum_order_amount", DbType.Decimal)]
        public decimal SumOrderAmount { get; set; }

        [pengdows.crud.attributes.Column("count_for_avg", DbType.Int64)]
        public long CountForAvg { get; set; }

        // Computed property for average
        public decimal AvgOrderAmount => CountForAvg > 0 ? SumOrderAmount / CountForAvg : 0;
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
                entity.ToTable("Customers");
                entity.HasKey(e => e.CustomerId);
                entity.Property(e => e.CustomerId).HasColumnName("customer_id");
                entity.Property(e => e.CompanyName).HasColumnName("company_name").IsRequired();
                entity.Property(e => e.CreatedDate).HasColumnName("created_date");
            });

            modelBuilder.Entity<EfOrder>(entity =>
            {
                entity.ToTable("Orders");
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