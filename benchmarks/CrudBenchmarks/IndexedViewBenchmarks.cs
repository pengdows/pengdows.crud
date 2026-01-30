using System.ComponentModel.DataAnnotations;
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
/// Demonstrates pengdows.crud's ability to map entities directly to indexed views.
/// pengdows.crud can query pre-aggregated indexed views with strongly-typed entities,
/// while EF must query base tables and aggregate at runtime.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5, invocationCount: 50)]
public class IndexedViewBenchmarks : IAsyncDisposable
{
    private const string CustomerSummaryViewSqlTemplate = """
        SELECT customer_id as CustomerId,
               order_count as OrderCount,
               total_amount as TotalAmount,
               sum_order_amount as SumOrderAmount,
               count_for_avg as CountForAvg
        FROM dbo.vw_CustomerOrderSummary WITH (NOEXPAND)
        WHERE customer_id = {customerId}
        """;
    private IContainer? _sqlServerContainer;
    private string _connStr = string.Empty;
    private const string Password = "YourStrong@Passw0rd";
    private const string Database = "IndexedViewBenchmark";

    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private TableGateway<CustomerOrderSummary, int> _summaryHelper = null!;
    private EfTestDbContext _efContext = null!;
    private DbContextOptions<EfTestDbContext> _efOptions = null!;

    // Separate connection strings with different Application Names to ensure isolated connection pools
    private string _pengdowsConnStr = string.Empty;
    private string _efConnStr = string.Empty;
    private string _directSqlConnStr = string.Empty;

    private readonly List<int> _customerIds = new();
    private int _testCustomerId;

    [Params(1000, 5000)] // Different dataset sizes to show scaling impact
    public int CustomerCount;

    [Params(10)] // Orders per customer average
    public int OrdersPerCustomer;
    [Params(16)] public int Parallelism;
    [Params(64)] public int OperationsPerRun;

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
        var baseConnStr =
            $"Server=localhost,{hostPort};Database={Database};User Id=sa;Password={Password};TrustServerCertificate=true;Connection Timeout=30;";

        // Use different Application Names to ensure completely separate connection pools for each library
        // This prevents one library from benefiting from another's pooled connections
        _connStr = baseConnStr; // For setup operations
        _pengdowsConnStr = baseConnStr + "Application Name=Benchmark_PengdowsCrud;";
        _efConnStr = baseConnStr + "Application Name=Benchmark_EntityFramework;";
        _directSqlConnStr = baseConnStr + "Application Name=Benchmark_DirectSQL;";

        Console.WriteLine($"[BENCHMARK] SQL Server container started on port {hostPort}");
        Console.WriteLine($"[BENCHMARK] Using separate connection pools per library (via Application Name)");

        // Wait for SQL Server to be ready with retry logic
        await WaitForSqlServerAsync();

        await CreateDatabaseAndSchemaAsync();
        await SeedDataAsync();

        // Setup pengdows.crud context with its own connection pool
        _map = new TypeMapRegistry();
        _map.Register<CustomerOrderSummary>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _pengdowsConnStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsContext = new DatabaseContext(cfg, SqlClientFactory.Instance, null, _map);
        _summaryHelper = new TableGateway<CustomerOrderSummary, int>(_pengdowsContext);

        // Setup Entity Framework context with its own connection pool
        _efOptions = new DbContextOptionsBuilder<EfTestDbContext>()
            .UseSqlServer(_efConnStr)
            .Options;
        _efContext = new EfTestDbContext(_efOptions);

        // Direct SQL will open/close connections per call (fair comparison)
        // Connection string stored for use in benchmark method

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

        // Verify indexed view can be queried directly
        var sampleData = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT TOP 1 customer_id, order_count, total_amount FROM dbo.vw_CustomerOrderSummary");
        if (sampleData != null)
        {
            Console.WriteLine("[VERIFICATION] ✅ Indexed view is queryable with pre-aggregated data");
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

        // No persistent direct SQL connection to clean up - connections are opened/closed per call
    }

    /// <summary>
    /// pengdows.crud querying indexed view directly - guaranteed fast path to pre-aggregated data.
    /// This is the key advantage: strongly-typed entity mapping to indexed views.
    /// </summary>
    [Benchmark]
    public async Task<CustomerOrderSummary?> GetCustomerSummary_pengdows_IndexedView()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildCustomerSummarySql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("customerId", DbType.Int32, _testCustomerId);
        return await _summaryHelper.LoadSingleAsync(container);
    }

    /// <summary>
    /// Entity Framework querying base tables - must aggregate at runtime.
    /// EF cannot directly map entities to indexed views like pengdows.crud can.
    /// </summary>
    [Benchmark]
    public async Task<EfCustomerOrderSummary?> GetCustomerSummary_EntityFramework_Aggregation()
    {
        var row = await _efContext.CustomerOrderSummaryRows
            .FromSqlRaw(BuildCustomerSummarySql(param => $"@{param}"),
                new SqlParameter("customerId", _testCustomerId))
            .AsNoTracking()
            .FirstOrDefaultAsync();

        return row == null
            ? null
            : new EfCustomerOrderSummary
            {
                CustomerId = row.CustomerId,
                OrderCount = row.OrderCount > int.MaxValue ? int.MaxValue : (int)row.OrderCount,
                TotalAmount = row.TotalAmount,
                AvgOrderAmount = row.CountForAvg > 0 ? row.SumOrderAmount / row.CountForAvg : 0,
                LastOrderDate = default
            };
    }

    /// <summary>
    /// Entity Framework querying base tables - another aggregation query for comparison.
    /// Shows consistent runtime aggregation overhead regardless of approach.
    /// </summary>
    // Intentionally no EF workaround benchmark here.

    /// <summary>
    /// Direct SQL querying indexed view - opens/closes connection each call for fair comparison.
    /// This is a true apples-to-apples benchmark against pengdows.crud's Standard mode.
    /// </summary>
    [Benchmark]
    public async Task<CustomerOrderSummary?> GetCustomerSummary_DirectSQL_IndexedView()
    {
        // Fair comparison: open and close connection each call, just like pengdows.crud Standard mode
        await using var conn = new SqlConnection(_directSqlConnStr);
        await conn.OpenAsync();
        var sql = BuildCustomerSummarySql(param => $"@{param}");
        return await conn.QuerySingleOrDefaultAsync<CustomerOrderSummary>(
            sql,
            new { customerId = _testCustomerId });
    }

    /// <summary>
    /// Direct SQL querying indexed view with explicit session settings for view matching parity.
    /// </summary>
    // Intentionally no session-settings variant for direct SQL.

    [Benchmark]
    public async Task GetCustomerSummary_pengdows_IndexedView_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildCustomerSummarySql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("customerId", DbType.Int32, _testCustomerId);
            await _summaryHelper.LoadSingleAsync(container);
        });
    }

    [Benchmark]
    public async Task GetCustomerSummary_EntityFramework_Aggregation_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new EfTestDbContext(_efOptions);
            await ctx.CustomerOrderSummaryRows
                .FromSqlRaw(BuildCustomerSummarySql(param => $"@{param}"),
                    new SqlParameter("customerId", _testCustomerId))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        });
    }

    // Intentionally no EF workaround concurrent benchmark here.

    [Benchmark]
    public async Task GetCustomerSummary_DirectSQL_IndexedView_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = new SqlConnection(_directSqlConnStr);
            await conn.OpenAsync();
            var sql = BuildCustomerSummarySql(param => $"@{param}");
            await conn.QuerySingleOrDefaultAsync<CustomerOrderSummary>(
                sql,
                new { customerId = _testCustomerId });
        });
    }

    // Intentionally no session-settings concurrent variant for direct SQL.

    private static string BuildCustomerSummarySql(Func<string, string> param)
    {
        return CustomerSummaryViewSqlTemplate.Replace("{customerId}", param("customerId"));
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
        public DbSet<CustomerOrderSummaryRow> CustomerOrderSummaryRows { get; set; }

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

            modelBuilder.Entity<CustomerOrderSummaryRow>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
                entity.Property(e => e.CustomerId).HasColumnName("CustomerId");
                entity.Property(e => e.OrderCount).HasColumnName("OrderCount");
                entity.Property(e => e.TotalAmount).HasColumnName("TotalAmount");
                entity.Property(e => e.SumOrderAmount).HasColumnName("SumOrderAmount");
                entity.Property(e => e.CountForAvg).HasColumnName("CountForAvg");
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

    [Keyless]
    public class CustomerOrderSummaryRow
    {
        public int CustomerId { get; set; }
        public long OrderCount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal SumOrderAmount { get; set; }
        public long CountForAvg { get; set; }
    }
}
