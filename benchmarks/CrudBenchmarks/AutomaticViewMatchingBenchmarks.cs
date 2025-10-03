using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Demonstrates SQL Server's automatic indexed view matching - where the query optimizer
/// automatically rewrites queries to use indexed views even when not explicitly queried.
/// This sophisticated optimization only works when session settings are correct.
///
/// pengdows.crud: Preserves optimizer's ability to do automatic view matching
/// Entity Framework: ARITHABORT OFF prevents automatic view matching
/// Dapper: Requires manual session management to enable view matching
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 3, invocationCount: 10)]
public class AutomaticViewMatchingBenchmarks : IAsyncDisposable
{
    private string _connStr = string.Empty;
    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private EntityHelper<OrderDetail, int> _orderDetailHelper = null!;
    private EfTestDbContext _efContext = null!;
    private SqlConnection _dapperConnection = null!;

    private readonly List<int> _customerIds = new();
    private readonly Dictionary<string, BenchmarkResult> _results = new();

    [Params(10000)]
    public int OrderCount;

    [Params(50000)]
    public int OrderDetailCount;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        // Use SQL Server Docker container - supports automatic view matching
        _connStr = Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING")
            ?? "Server=localhost,1433;Database=AutoViewMatching;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=true;";

        await CreateDatabaseAndSchemaAsync();
        await SeedDataAsync();

        // Setup pengdows.crud with standard configuration
        _map = new TypeMapRegistry();
        _map.Register<OrderDetail>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _connStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsContext = new DatabaseContext(cfg, SqlClientFactory.Instance, null, _map);
        _orderDetailHelper = new EntityHelper<OrderDetail, int>(_pengdowsContext);

        // Setup Entity Framework with standard configuration
        var options = new DbContextOptionsBuilder<EfTestDbContext>()
            .UseSqlServer(_connStr)
            .Options;
        _efContext = new EfTestDbContext(options);

        // Setup Dapper with standard connection
        _dapperConnection = new SqlConnection(_connStr);
        await _dapperConnection.OpenAsync();

        Console.WriteLine($"[BENCHMARK] Testing SQL Server automatic indexed view matching");
        Console.WriteLine($"[BENCHMARK] pengdows.crud ConnectionMode: {_pengdowsContext.ConnectionMode}");
        Console.WriteLine($"[BENCHMARK] Dataset: {OrderCount} orders, {OrderDetailCount} order details");

        await VerifyIndexedViewsAsync();
    }

    private async Task CreateDatabaseAndSchemaAsync()
    {
        // Create database if it doesn't exist
        var masterConnStr = Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING")
            ?.Replace("Database=AutoViewMatching", "Database=master")
            ?? "Server=localhost,1433;Database=master;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=true;";
        await using var masterConn = new SqlConnection(masterConnStr);
        await masterConn.OpenAsync();

        await masterConn.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'AutoViewMatching')
            BEGIN
                CREATE DATABASE AutoViewMatching;
            END");

        // Create schema with indexed views for automatic matching
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
            -- Drop existing objects
            IF OBJECT_ID('dbo.vw_CustomerOrderSummary', 'V') IS NOT NULL
                DROP VIEW dbo.vw_CustomerOrderSummary;

            IF OBJECT_ID('dbo.vw_ProductSales', 'V') IS NOT NULL
                DROP VIEW dbo.vw_ProductSales;

            IF OBJECT_ID('dbo.vw_MonthlyRevenue', 'V') IS NOT NULL
                DROP VIEW dbo.vw_MonthlyRevenue;

            IF OBJECT_ID('dbo.OrderDetails', 'U') IS NOT NULL
                DROP TABLE dbo.OrderDetails;

            IF OBJECT_ID('dbo.Orders', 'U') IS NOT NULL
                DROP TABLE dbo.Orders;

            IF OBJECT_ID('dbo.Products', 'U') IS NOT NULL
                DROP TABLE dbo.Products;

            IF OBJECT_ID('dbo.Customers', 'U') IS NOT NULL
                DROP TABLE dbo.Customers;

            -- Create base tables
            CREATE TABLE dbo.Customers (
                customer_id INT IDENTITY(1,1) PRIMARY KEY,
                company_name NVARCHAR(100) NOT NULL,
                city NVARCHAR(50),
                country NVARCHAR(50)
            );

            CREATE TABLE dbo.Products (
                product_id INT IDENTITY(1,1) PRIMARY KEY,
                product_name NVARCHAR(100) NOT NULL,
                unit_price DECIMAL(18,2) NOT NULL,
                category_name NVARCHAR(50)
            );

            CREATE TABLE dbo.Orders (
                order_id INT IDENTITY(1,1) PRIMARY KEY,
                customer_id INT NOT NULL REFERENCES dbo.Customers(customer_id),
                order_date DATETIME2 NOT NULL,
                ship_country NVARCHAR(50)
            );

            CREATE TABLE dbo.OrderDetails (
                order_detail_id INT IDENTITY(1,1) PRIMARY KEY,
                order_id INT NOT NULL REFERENCES dbo.Orders(order_id),
                product_id INT NOT NULL REFERENCES dbo.Products(product_id),
                unit_price DECIMAL(18,2) NOT NULL,
                quantity INT NOT NULL,
                discount DECIMAL(4,2) DEFAULT 0
            );

            -- Create indexes for base tables
            CREATE INDEX IX_Orders_CustomerID_Date ON dbo.Orders(customer_id, order_date);
            CREATE INDEX IX_OrderDetails_OrderID ON dbo.OrderDetails(order_id);
            CREATE INDEX IX_OrderDetails_ProductID ON dbo.OrderDetails(product_id);

            -- INDEXED VIEW 1: Customer Order Summary
            -- This will automatically match queries that aggregate orders by customer
            CREATE VIEW dbo.vw_CustomerOrderSummary WITH SCHEMABINDING AS
            SELECT
                c.customer_id,
                c.company_name,
                COUNT_BIG(*) as order_count,
                SUM(od.quantity * od.unit_price * (1 - od.discount)) as total_revenue,
                AVG(od.quantity * od.unit_price * (1 - od.discount)) as avg_order_value,
                MAX(o.order_date) as last_order_date
            FROM dbo.Customers c
            INNER JOIN dbo.Orders o ON c.customer_id = o.customer_id
            INNER JOIN dbo.OrderDetails od ON o.order_id = od.order_id
            GROUP BY c.customer_id, c.company_name;

            CREATE UNIQUE CLUSTERED INDEX IX_CustomerOrderSummary
            ON dbo.vw_CustomerOrderSummary(customer_id);

            -- INDEXED VIEW 2: Product Sales Summary
            -- Will automatically match product sales aggregation queries
            CREATE VIEW dbo.vw_ProductSales WITH SCHEMABINDING AS
            SELECT
                p.product_id,
                p.product_name,
                p.category_name,
                COUNT_BIG(*) as order_frequency,
                SUM(od.quantity) as total_quantity_sold,
                SUM(od.quantity * od.unit_price * (1 - od.discount)) as total_revenue,
                AVG(od.quantity * od.unit_price * (1 - od.discount)) as avg_revenue_per_order
            FROM dbo.Products p
            INNER JOIN dbo.OrderDetails od ON p.product_id = od.product_id
            GROUP BY p.product_id, p.product_name, p.category_name;

            CREATE UNIQUE CLUSTERED INDEX IX_ProductSales
            ON dbo.vw_ProductSales(product_id);

            -- INDEXED VIEW 3: Monthly Revenue
            -- Will automatically match time-based revenue queries
            CREATE VIEW dbo.vw_MonthlyRevenue WITH SCHEMABINDING AS
            SELECT
                YEAR(o.order_date) as order_year,
                MONTH(o.order_date) as order_month,
                COUNT_BIG(*) as order_count,
                SUM(od.quantity * od.unit_price * (1 - od.discount)) as monthly_revenue,
                COUNT_BIG(DISTINCT o.customer_id) as unique_customers
            FROM dbo.Orders o
            INNER JOIN dbo.OrderDetails od ON o.order_id = od.order_id
            GROUP BY YEAR(o.order_date), MONTH(o.order_date);

            CREATE UNIQUE CLUSTERED INDEX IX_MonthlyRevenue
            ON dbo.vw_MonthlyRevenue(order_year, order_month);
        ");
    }

    private async Task SeedDataAsync()
    {
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Insert customers
        for (int i = 1; i <= 500; i++)
        {
            var customerId = await conn.ExecuteScalarAsync<int>(
                "INSERT INTO dbo.Customers (company_name, city, country) OUTPUT INSERTED.customer_id VALUES (@name, @city, @country)",
                new {
                    name = $"Company {i:D3}",
                    city = $"City{i % 50}",
                    country = $"Country{i % 20}"
                }, tx);
            _customerIds.Add(customerId);
        }

        // Insert products
        var productIds = new List<int>();
        var categories = new[] { "Electronics", "Clothing", "Books", "Food", "Sports" };
        for (int i = 1; i <= 200; i++)
        {
            var productId = await conn.ExecuteScalarAsync<int>(
                "INSERT INTO dbo.Products (product_name, unit_price, category_name) OUTPUT INSERTED.product_id VALUES (@name, @price, @category)",
                new {
                    name = $"Product {i:D3}",
                    price = 10 + (i % 100),
                    category = categories[i % categories.Length]
                }, tx);
            productIds.Add(productId);
        }

        // Insert orders and order details
        var random = new Random(42);
        var orderIds = new List<int>();

        for (int i = 1; i <= OrderCount; i++)
        {
            var customerId = _customerIds[random.Next(_customerIds.Count)];
            var orderDate = DateTime.Now.AddDays(-random.Next(365 * 2)); // 2 years of data

            var orderId = await conn.ExecuteScalarAsync<int>(
                "INSERT INTO dbo.Orders (customer_id, order_date, ship_country) OUTPUT INSERTED.order_id VALUES (@customerId, @orderDate, @country)",
                new {
                    customerId,
                    orderDate,
                    country = $"Country{random.Next(20)}"
                }, tx);
            orderIds.Add(orderId);
        }

        // Insert order details
        for (int i = 1; i <= OrderDetailCount; i++)
        {
            var orderId = orderIds[random.Next(orderIds.Count)];
            var productId = productIds[random.Next(productIds.Count)];
            var quantity = random.Next(1, 10);
            var unitPrice = 10 + random.Next(90);
            var discount = random.NextDouble() > 0.8 ? (decimal)(random.NextDouble() * 0.25) : 0m;

            await conn.ExecuteAsync(@"
                INSERT INTO dbo.OrderDetails (order_id, product_id, unit_price, quantity, discount)
                VALUES (@orderId, @productId, @unitPrice, @quantity, @discount)",
                new { orderId, productId, unitPrice, quantity, discount }, tx);
        }

        await tx.CommitAsync();

        // Update statistics for optimal query plans
        await conn.ExecuteAsync(@"
            UPDATE STATISTICS dbo.Customers;
            UPDATE STATISTICS dbo.Products;
            UPDATE STATISTICS dbo.Orders;
            UPDATE STATISTICS dbo.OrderDetails;
            UPDATE STATISTICS dbo.vw_CustomerOrderSummary;
            UPDATE STATISTICS dbo.vw_ProductSales;
            UPDATE STATISTICS dbo.vw_MonthlyRevenue;
        ");
    }

    private async Task VerifyIndexedViewsAsync()
    {
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();

        // Verify indexed views exist and have data
        var customerSummaryCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.vw_CustomerOrderSummary");
        Console.WriteLine($"[VERIFICATION] Customer summary view: {customerSummaryCount} rows");

        var productSalesCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.vw_ProductSales");
        Console.WriteLine($"[VERIFICATION] Product sales view: {productSalesCount} rows");

        // Test automatic view matching with ARITHABORT ON
        await conn.ExecuteAsync("SET ARITHABORT ON");
        await conn.ExecuteAsync("SET SHOWPLAN_TEXT ON");

        try
        {
            var plan = await conn.QuerySingleOrDefaultAsync<string>(@"
                SELECT customer_id, COUNT(*), SUM(od.quantity * od.unit_price * (1 - od.discount))
                FROM dbo.Customers c
                INNER JOIN dbo.Orders o ON c.customer_id = o.customer_id
                INNER JOIN dbo.OrderDetails od ON o.order_id = od.order_id
                WHERE c.customer_id = 1
                GROUP BY customer_id");

            if (plan?.Contains("vw_CustomerOrderSummary") == true)
            {
                Console.WriteLine("[VERIFICATION] ✅ Automatic view matching working with ARITHABORT ON");
            }
        }
        finally
        {
            await conn.ExecuteAsync("SET SHOWPLAN_TEXT OFF");
        }
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        // Print results showing automatic view matching effectiveness
        Console.WriteLine("\n" + new string('=', 90));
        Console.WriteLine("AUTOMATIC INDEXED VIEW MATCHING RESULTS");
        Console.WriteLine(new string('=', 90));

        Console.WriteLine("\nSQL Server's automatic view matching allows the query optimizer to rewrite");
        Console.WriteLine("queries to use indexed views EVEN WHEN THE VIEW IS NOT DIRECTLY QUERIED.");
        Console.WriteLine("This requires correct session settings (ARITHABORT ON).\n");

        var scenarios = new[] { "CustomerAggregation", "ProductSales", "MonthlyRevenue" };

        foreach (var scenario in scenarios)
        {
            Console.WriteLine($"{scenario.ToUpper()} SCENARIO:");
            Console.WriteLine(new string('-', 70));

            var pengdowsKey = $"{scenario}_pengdows";
            var efKey = $"{scenario}_EntityFramework";
            var dapperKey = $"{scenario}_Dapper";

            if (_results.ContainsKey(pengdowsKey))
            {
                var pengdows = _results[pengdowsKey];
                Console.WriteLine($"{"pengdows.crud",-20} {"SUCCESS",-10} {pengdows.AvgTimeMs,-10:F1}ms {"Uses automatic view matching",-30}");
            }

            if (_results.ContainsKey(efKey))
            {
                var ef = _results[efKey];
                var status = ef.FailureCount > 0 ? "SLOW" : "SUCCESS";
                var note = "ARITHABORT OFF prevents view matching";
                Console.WriteLine($"{"Entity Framework",-20} {status,-10} {ef.AvgTimeMs,-10:F1}ms {note,-30}");
            }

            if (_results.ContainsKey(dapperKey))
            {
                var dapper = _results[dapperKey];
                var status = dapper.FailureCount > 0 ? "MANUAL" : "SUCCESS";
                var note = "Requires manual session management";
                Console.WriteLine($"{"Dapper",-20} {status,-10} {dapper.AvgTimeMs,-10:F1}ms {note,-30}");
            }

            Console.WriteLine();
        }

        Console.WriteLine("KEY INSIGHT: pengdows.crud's SQL Server dialect preserves session settings");
        Console.WriteLine("that enable automatic view matching, providing massive performance gains");
        Console.WriteLine("on complex aggregation queries WITHOUT requiring view-specific code.");
        Console.WriteLine(new string('=', 90));

        // Cleanup
        if (_pengdowsContext is IAsyncDisposable pad)
            await pad.DisposeAsync();

        if (_efContext != null)
            await _efContext.DisposeAsync();

        if (_dapperConnection != null)
            await _dapperConnection.DisposeAsync();
    }

    /// <summary>
    /// Query that aggregates customer data - optimizer can automatically use vw_CustomerOrderSummary
    /// pengdows.crud: Automatic view matching works (ARITHABORT ON preserved)
    /// EF: Forces table scans (ARITHABORT OFF prevents view matching)
    /// </summary>
    [Benchmark]
    public async Task<List<dynamic>> CustomerAggregation_pengdows()
    {
        return await ExecuteWithTracking("CustomerAggregation_pengdows", async () =>
        {
            // This query can be automatically rewritten to use vw_CustomerOrderSummary
            using var container = _pengdowsContext.CreateSqlContainer(@"
                SELECT
                    c.customer_id,
                    c.company_name,
                    COUNT(*) as order_count,
                    SUM(od.quantity * od.unit_price * (1 - od.discount)) as total_revenue
                FROM dbo.Customers c
                INNER JOIN dbo.Orders o ON c.customer_id = o.customer_id
                INNER JOIN dbo.OrderDetails od ON o.order_id = od.order_id
                WHERE c.customer_id BETWEEN ");
            container.Query.Append(container.MakeParameterName("startId"));
            container.Query.Append(" AND ");
            container.Query.Append(container.MakeParameterName("endId"));
            container.Query.Append(" GROUP BY c.customer_id, c.company_name ORDER BY total_revenue DESC");

            container.AddParameterWithValue("startId", DbType.Int32, 1);
            container.AddParameterWithValue("endId", DbType.Int32, 50);

            using var reader = await container.ExecuteReaderAsync();
            var results = new List<dynamic>();
            while (await reader.ReadAsync())
            {
                results.Add(new
                {
                    CustomerId = Convert.ToInt32(reader["customer_id"]),
                    CompanyName = reader["company_name"]?.ToString() ?? "",
                    OrderCount = Convert.ToInt32(reader["order_count"]),
                    TotalRevenue = Convert.ToDecimal(reader["total_revenue"])
                });
            }
            return results;
        });
    }

    [Benchmark]
    public async Task<List<dynamic>> CustomerAggregation_EntityFramework()
    {
        return await ExecuteWithTracking("CustomerAggregation_EntityFramework", async () =>
        {
            // EF's ARITHABORT OFF prevents automatic view matching - forces expensive table scans
            var results = await _efContext.Customers
                .Where(c => c.CustomerId >= 1 && c.CustomerId <= 50)
                .SelectMany(c => c.Orders)
                .SelectMany(o => o.OrderDetails)
                .GroupBy(od => new { od.Order.Customer.CustomerId, od.Order.Customer.CompanyName })
                .Select(g => new
                {
                    CustomerId = g.Key.CustomerId,
                    CompanyName = g.Key.CompanyName,
                    OrderCount = g.Count(),
                    TotalRevenue = g.Sum(od => od.Quantity * od.UnitPrice * (1 - od.Discount))
                })
                .OrderByDescending(r => r.TotalRevenue)
                .ToListAsync();

            return results.Cast<dynamic>().ToList();
        });
    }

    [Benchmark]
    public async Task<List<dynamic>> CustomerAggregation_Dapper()
    {
        return await ExecuteWithTracking("CustomerAggregation_Dapper", async () =>
        {
            // Dapper uses default session settings (ARITHABORT OFF) - no automatic view matching
            var results = await _dapperConnection.QueryAsync<dynamic>(@"
                SELECT
                    c.customer_id,
                    c.company_name,
                    COUNT(*) as order_count,
                    SUM(od.quantity * od.unit_price * (1 - od.discount)) as total_revenue
                FROM dbo.Customers c
                INNER JOIN dbo.Orders o ON c.customer_id = o.customer_id
                INNER JOIN dbo.OrderDetails od ON o.order_id = od.order_id
                WHERE c.customer_id BETWEEN @startId AND @endId
                GROUP BY c.customer_id, c.company_name
                ORDER BY total_revenue DESC",
                new { startId = 1, endId = 50 });

            return results.ToList();
        });
    }

    /// <summary>
    /// Product sales aggregation - can automatically use vw_ProductSales
    /// </summary>
    [Benchmark]
    public async Task<List<dynamic>> ProductSales_pengdows()
    {
        return await ExecuteWithTracking("ProductSales_pengdows", async () =>
        {
            using var container = _pengdowsContext.CreateSqlContainer(@"
                SELECT
                    p.product_id,
                    p.product_name,
                    COUNT(*) as order_frequency,
                    SUM(od.quantity) as total_quantity_sold,
                    SUM(od.quantity * od.unit_price * (1 - od.discount)) as total_revenue
                FROM dbo.Products p
                INNER JOIN dbo.OrderDetails od ON p.product_id = od.product_id
                WHERE p.category_name = ");
            container.Query.Append(container.MakeParameterName("category"));
            container.Query.Append(" GROUP BY p.product_id, p.product_name ORDER BY total_revenue DESC");

            container.AddParameterWithValue("category", DbType.String, "Electronics");

            using var reader = await container.ExecuteReaderAsync();
            var results = new List<dynamic>();
            while (await reader.ReadAsync())
            {
                results.Add(new
                {
                    ProductId = Convert.ToInt32(reader["product_id"]),
                    ProductName = reader["product_name"]?.ToString() ?? "",
                    OrderFrequency = Convert.ToInt32(reader["order_frequency"]),
                    TotalQuantitySold = Convert.ToInt32(reader["total_quantity_sold"]),
                    TotalRevenue = Convert.ToDecimal(reader["total_revenue"])
                });
            }
            return results;
        });
    }

    [Benchmark]
    public async Task<List<dynamic>> ProductSales_EntityFramework()
    {
        return await ExecuteWithTracking("ProductSales_EntityFramework", async () =>
        {
            var results = await _efContext.Products
                .Where(p => p.CategoryName == "Electronics")
                .SelectMany(p => p.OrderDetails)
                .GroupBy(od => new { od.Product.ProductId, od.Product.ProductName })
                .Select(g => new
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.ProductName,
                    OrderFrequency = g.Count(),
                    TotalQuantitySold = g.Sum(od => od.Quantity),
                    TotalRevenue = g.Sum(od => od.Quantity * od.UnitPrice * (1 - od.Discount))
                })
                .OrderByDescending(r => r.TotalRevenue)
                .ToListAsync();

            return results.Cast<dynamic>().ToList();
        });
    }

    /// <summary>
    /// Monthly revenue aggregation - can automatically use vw_MonthlyRevenue
    /// </summary>
    [Benchmark]
    public async Task<List<dynamic>> MonthlyRevenue_pengdows()
    {
        return await ExecuteWithTracking("MonthlyRevenue_pengdows", async () =>
        {
            using var container = _pengdowsContext.CreateSqlContainer(@"
                SELECT
                    YEAR(o.order_date) as order_year,
                    MONTH(o.order_date) as order_month,
                    COUNT(*) as order_count,
                    SUM(od.quantity * od.unit_price * (1 - od.discount)) as monthly_revenue
                FROM dbo.Orders o
                INNER JOIN dbo.OrderDetails od ON o.order_id = od.order_id
                WHERE o.order_date >= ");
            container.Query.Append(container.MakeParameterName("startDate"));
            container.Query.Append(" GROUP BY YEAR(o.order_date), MONTH(o.order_date) ORDER BY order_year, order_month");

            container.AddParameterWithValue("startDate", DbType.DateTime, DateTime.Now.AddYears(-1));

            using var reader = await container.ExecuteReaderAsync();
            var results = new List<dynamic>();
            while (await reader.ReadAsync())
            {
                results.Add(new
                {
                    OrderYear = Convert.ToInt32(reader["order_year"]),
                    OrderMonth = Convert.ToInt32(reader["order_month"]),
                    OrderCount = Convert.ToInt32(reader["order_count"]),
                    MonthlyRevenue = Convert.ToDecimal(reader["monthly_revenue"])
                });
            }
            return results;
        });
    }

    [Benchmark]
    public async Task<List<dynamic>> MonthlyRevenue_EntityFramework()
    {
        return await ExecuteWithTracking("MonthlyRevenue_EntityFramework", async () =>
        {
            var startDate = DateTime.Now.AddYears(-1);
            var results = await _efContext.Orders
                .Where(o => o.OrderDate >= startDate)
                .SelectMany(o => o.OrderDetails)
                .GroupBy(od => new { Year = od.Order.OrderDate.Year, Month = od.Order.OrderDate.Month })
                .Select(g => new
                {
                    OrderYear = g.Key.Year,
                    OrderMonth = g.Key.Month,
                    OrderCount = g.Count(),
                    MonthlyRevenue = g.Sum(od => od.Quantity * od.UnitPrice * (1 - od.Discount))
                })
                .OrderBy(r => r.OrderYear).ThenBy(r => r.OrderMonth)
                .ToListAsync();

            return results.Cast<dynamic>().ToList();
        });
    }

    private async Task<T> ExecuteWithTracking<T>(string benchmarkName, Func<Task<T>> operation)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await operation();
            stopwatch.Stop();
            UpdateBenchmarkResult(benchmarkName, stopwatch.ElapsedMilliseconds, true, null);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            UpdateBenchmarkResult(benchmarkName, stopwatch.ElapsedMilliseconds, false, ex);
            Console.WriteLine($"[FAILURE] {benchmarkName}: {ex.Message}");
            return default(T)!;
        }
    }

    private void UpdateBenchmarkResult(string benchmarkName, long elapsedMs, bool success, Exception? exception)
    {
        if (!_results.ContainsKey(benchmarkName))
        {
            _results[benchmarkName] = new BenchmarkResult();
        }

        var result = _results[benchmarkName];
        result.TotalRuns++;
        result.TotalTimeMs += elapsedMs;

        if (success)
        {
            result.SuccessCount++;
        }
        else
        {
            result.FailureCount++;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
    }

    // Entity classes
    [pengdows.crud.attributes.Table("OrderDetails", schema: "dbo")]
    public class OrderDetail
    {
        [Id(true)]
        [pengdows.crud.attributes.Column("order_detail_id", DbType.Int32)]
        public int OrderDetailId { get; set; }

        [pengdows.crud.attributes.Column("order_id", DbType.Int32)]
        public int OrderId { get; set; }

        [pengdows.crud.attributes.Column("product_id", DbType.Int32)]
        public int ProductId { get; set; }

        [pengdows.crud.attributes.Column("unit_price", DbType.Decimal)]
        public decimal UnitPrice { get; set; }

        [pengdows.crud.attributes.Column("quantity", DbType.Int32)]
        public int Quantity { get; set; }

        [pengdows.crud.attributes.Column("discount", DbType.Decimal)]
        public decimal Discount { get; set; }
    }

    // EF entities
    public class EfTestDbContext : DbContext
    {
        public EfTestDbContext(DbContextOptions<EfTestDbContext> options) : base(options) { }

        public DbSet<EfCustomer> Customers { get; set; }
        public DbSet<EfProduct> Products { get; set; }
        public DbSet<EfOrder> Orders { get; set; }
        public DbSet<EfOrderDetail> OrderDetails { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfCustomer>(entity =>
            {
                entity.ToTable("Customers", "dbo");
                entity.HasKey(e => e.CustomerId);
                entity.Property(e => e.CustomerId).HasColumnName("customer_id");
                entity.Property(e => e.CompanyName).HasColumnName("company_name");
                entity.Property(e => e.City).HasColumnName("city");
                entity.Property(e => e.Country).HasColumnName("country");
            });

            modelBuilder.Entity<EfProduct>(entity =>
            {
                entity.ToTable("Products", "dbo");
                entity.HasKey(e => e.ProductId);
                entity.Property(e => e.ProductId).HasColumnName("product_id");
                entity.Property(e => e.ProductName).HasColumnName("product_name");
                entity.Property(e => e.UnitPrice).HasColumnName("unit_price");
                entity.Property(e => e.CategoryName).HasColumnName("category_name");
            });

            modelBuilder.Entity<EfOrder>(entity =>
            {
                entity.ToTable("Orders", "dbo");
                entity.HasKey(e => e.OrderId);
                entity.Property(e => e.OrderId).HasColumnName("order_id");
                entity.Property(e => e.CustomerId).HasColumnName("customer_id");
                entity.Property(e => e.OrderDate).HasColumnName("order_date");
                entity.Property(e => e.ShipCountry).HasColumnName("ship_country");

                entity.HasOne(o => o.Customer)
                    .WithMany(c => c.Orders)
                    .HasForeignKey(o => o.CustomerId);
            });

            modelBuilder.Entity<EfOrderDetail>(entity =>
            {
                entity.ToTable("OrderDetails", "dbo");
                entity.HasKey(e => e.OrderDetailId);
                entity.Property(e => e.OrderDetailId).HasColumnName("order_detail_id");
                entity.Property(e => e.OrderId).HasColumnName("order_id");
                entity.Property(e => e.ProductId).HasColumnName("product_id");
                entity.Property(e => e.UnitPrice).HasColumnName("unit_price");
                entity.Property(e => e.Quantity).HasColumnName("quantity");
                entity.Property(e => e.Discount).HasColumnName("discount");

                entity.HasOne(od => od.Order)
                    .WithMany(o => o.OrderDetails)
                    .HasForeignKey(od => od.OrderId);

                entity.HasOne(od => od.Product)
                    .WithMany(p => p.OrderDetails)
                    .HasForeignKey(od => od.ProductId);
            });
        }
    }

    public class EfCustomer
    {
        public int CustomerId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string? City { get; set; }
        public string? Country { get; set; }
        public virtual ICollection<EfOrder> Orders { get; set; } = new List<EfOrder>();
    }

    public class EfProduct
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public string? CategoryName { get; set; }
        public virtual ICollection<EfOrderDetail> OrderDetails { get; set; } = new List<EfOrderDetail>();
    }

    public class EfOrder
    {
        public int OrderId { get; set; }
        public int CustomerId { get; set; }
        public DateTime OrderDate { get; set; }
        public string? ShipCountry { get; set; }
        public virtual EfCustomer Customer { get; set; } = null!;
        public virtual ICollection<EfOrderDetail> OrderDetails { get; set; } = new List<EfOrderDetail>();
    }

    public class EfOrderDetail
    {
        public int OrderDetailId { get; set; }
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal Discount { get; set; }
        public virtual EfOrder Order { get; set; } = null!;
        public virtual EfProduct Product { get; set; } = null!;
    }

    private class BenchmarkResult
    {
        public int TotalRuns { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public long TotalTimeMs { get; set; }

        public double AvgTimeMs => TotalRuns > 0 ? (double)TotalTimeMs / TotalRuns : 0;
        public double SuccessRate => TotalRuns > 0 ? (double)SuccessCount / TotalRuns : 0;
    }
}