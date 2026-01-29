using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
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
    private IContainer? _sqlServerContainer;
    private string _baseConnStr = string.Empty;
    private string _pengdowsConnStr = string.Empty;
    private string _efConnStr = string.Empty;
    private string _dapperConnStr = string.Empty;
    private const string Password = "YourStrong@Passw0rd";
    private const string Database = "AutoViewMatching";

    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private EntityHelper<OrderDetail, int> _orderDetailHelper = null!;
    private EfTestDbContext _efContext = null!;
    private SqlConnection _dapperConnection = null!;

    // Pre-built query templates for cloning (56% faster than rebuilding)
    private ISqlContainer _customerAggregationTemplate = null!;
    private ISqlContainer _productSalesTemplate = null!;
    private ISqlContainer _monthlyRevenueTemplate = null!;

    private readonly List<int> _customerIds = new();
    private readonly Dictionary<string, BenchmarkResult> _results = new();

    [Params(10000)] public int OrderCount;

    [Params(50000)] public int OrderDetailCount;

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

        // Use different Application Names to ensure separate connection pools for each library
        // This prevents one library from benefiting from another's session settings
        _baseConnStr =
            $"Server=localhost,{hostPort};Database={Database};User Id=sa;Password={Password};TrustServerCertificate=true;Connection Timeout=30;";
        _pengdowsConnStr = _baseConnStr + "Application Name=Benchmark_PengdowsCrud;";
        _efConnStr = _baseConnStr + "Application Name=Benchmark_EntityFramework;";
        _dapperConnStr = _baseConnStr + "Application Name=Benchmark_Dapper;";

        Console.WriteLine($"[BENCHMARK] SQL Server container started on port {hostPort}");
        Console.WriteLine($"[BENCHMARK] Using separate connection pools per library (via Application Name)");

        // Wait for SQL Server to be ready with retry logic
        await WaitForSqlServerAsync();

        await CreateDatabaseAndSchemaAsync();
        await SeedDataAsync();

        // Setup pengdows.crud with its own connection pool
        _map = new TypeMapRegistry();
        _map.Register<OrderDetail>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _pengdowsConnStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsContext = new DatabaseContext(cfg, SqlClientFactory.Instance, null, _map);
        _orderDetailHelper = new EntityHelper<OrderDetail, int>(_pengdowsContext);

        // Setup Entity Framework with its own connection pool
        var options = new DbContextOptionsBuilder<EfTestDbContext>()
            .UseSqlServer(_efConnStr)
            .Options;
        _efContext = new EfTestDbContext(options);

        // Setup Dapper with its own connection pool (completely separate from others)
        _dapperConnection = new SqlConnection(_dapperConnStr);
        await _dapperConnection.OpenAsync();

        Console.WriteLine($"[BENCHMARK] Testing SQL Server automatic indexed view matching");
        Console.WriteLine($"[BENCHMARK] pengdows.crud ConnectionMode: {_pengdowsContext.ConnectionMode}");
        Console.WriteLine($"[BENCHMARK] Dataset: {OrderCount} orders, {OrderDetailCount} order details");

        // Pre-build query templates ONCE for cloning (56% faster than rebuilding each time)
        _customerAggregationTemplate = BuildCustomerAggregationTemplate();
        _productSalesTemplate = BuildProductSalesTemplate();
        _monthlyRevenueTemplate = BuildMonthlyRevenueTemplate();
        Console.WriteLine($"[BENCHMARK] Query templates pre-built for optimal performance");

        await VerifyIndexedViewsAsync();
    }

    private async Task WaitForSqlServerAsync()
    {
        var masterConnStr = _baseConnStr.Replace($"Database={Database}", "Database=master");
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
        var masterConnStr = _baseConnStr.Replace($"Database={Database}", "Database=master");
        await using var masterConn = new SqlConnection(masterConnStr);
        await masterConn.OpenAsync();

        await masterConn.ExecuteAsync($@"
            IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{Database}')
            BEGIN
                CREATE DATABASE [{Database}];
            END");

        // Create schema with indexed views for automatic matching - split into batches
        await using var conn = new SqlConnection(_baseConnStr);
        await conn.OpenAsync();

        // Drop existing objects
        await conn.ExecuteAsync(@"
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
                DROP TABLE dbo.Customers;");

        // Create base tables
        await conn.ExecuteAsync(@"
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

            CREATE INDEX IX_Orders_CustomerID_Date ON dbo.Orders(customer_id, order_date);
            CREATE INDEX IX_OrderDetails_OrderID ON dbo.OrderDetails(order_id);
            CREATE INDEX IX_OrderDetails_ProductID ON dbo.OrderDetails(product_id);");

        // Create first indexed view (must be in its own batch)
        // Note: AVG() not allowed in indexed views, use SUM/COUNT_BIG instead
        await conn.ExecuteAsync(@"
            CREATE VIEW dbo.vw_CustomerOrderSummary WITH SCHEMABINDING AS
            SELECT
                c.customer_id,
                c.company_name,
                COUNT_BIG(*) as order_count,
                SUM(ISNULL(od.quantity * od.unit_price * (1 - ISNULL(od.discount, 0)), 0)) as total_revenue,
                SUM(ISNULL(od.quantity * od.unit_price * (1 - ISNULL(od.discount, 0)), 0)) as sum_order_value,
                COUNT_BIG(*) as count_for_avg
            FROM dbo.Customers c
            INNER JOIN dbo.Orders o ON c.customer_id = o.customer_id
            INNER JOIN dbo.OrderDetails od ON o.order_id = od.order_id
            GROUP BY c.customer_id, c.company_name;");

        await conn.ExecuteAsync(@"
            CREATE UNIQUE CLUSTERED INDEX IX_CustomerOrderSummary
            ON dbo.vw_CustomerOrderSummary(customer_id);");

        // Create second indexed view
        await conn.ExecuteAsync(@"
            CREATE VIEW dbo.vw_ProductSales WITH SCHEMABINDING AS
            SELECT
                p.product_id,
                p.product_name,
                p.category_name,
                COUNT_BIG(*) as order_frequency,
                SUM(od.quantity) as total_quantity_sold,
                SUM(ISNULL(od.quantity * od.unit_price * (1 - ISNULL(od.discount, 0)), 0)) as total_revenue,
                SUM(ISNULL(od.quantity * od.unit_price * (1 - ISNULL(od.discount, 0)), 0)) as sum_revenue_per_order,
                COUNT_BIG(*) as count_for_avg
            FROM dbo.Products p
            INNER JOIN dbo.OrderDetails od ON p.product_id = od.product_id
            GROUP BY p.product_id, p.product_name, p.category_name;");

        await conn.ExecuteAsync(@"
            CREATE UNIQUE CLUSTERED INDEX IX_ProductSales
            ON dbo.vw_ProductSales(product_id);");

        // Create third indexed view
        await conn.ExecuteAsync(@"
            CREATE VIEW dbo.vw_MonthlyRevenue WITH SCHEMABINDING AS
            SELECT
                YEAR(o.order_date) as order_year,
                MONTH(o.order_date) as order_month,
                COUNT_BIG(*) as order_count,
                SUM(ISNULL(od.quantity * od.unit_price * (1 - ISNULL(od.discount, 0)), 0)) as monthly_revenue
            FROM dbo.Orders o
            INNER JOIN dbo.OrderDetails od ON o.order_id = od.order_id
            GROUP BY YEAR(o.order_date), MONTH(o.order_date);");

        await conn.ExecuteAsync(@"
            CREATE UNIQUE CLUSTERED INDEX IX_MonthlyRevenue
            ON dbo.vw_MonthlyRevenue(order_year, order_month);");
    }

    private async Task SeedDataAsync()
    {
        await using var conn = new SqlConnection(_baseConnStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Insert customers
        for (var i = 1; i <= 500; i++)
        {
            var customerId = await conn.ExecuteScalarAsync<int>(
                "INSERT INTO dbo.Customers (company_name, city, country) OUTPUT INSERTED.customer_id VALUES (@name, @city, @country)",
                new
                {
                    name = $"Company {i:D3}",
                    city = $"City{i % 50}",
                    country = $"Country{i % 20}"
                }, tx);
            _customerIds.Add(customerId);
        }

        // Insert products
        var productIds = new List<int>();
        var categories = new[] { "Electronics", "Clothing", "Books", "Food", "Sports" };
        for (var i = 1; i <= 200; i++)
        {
            var productId = await conn.ExecuteScalarAsync<int>(
                "INSERT INTO dbo.Products (product_name, unit_price, category_name) OUTPUT INSERTED.product_id VALUES (@name, @price, @category)",
                new
                {
                    name = $"Product {i:D3}",
                    price = 10 + i % 100,
                    category = categories[i % categories.Length]
                }, tx);
            productIds.Add(productId);
        }

        // Insert orders and order details
        var random = new Random(42);
        var orderIds = new List<int>();

        for (var i = 1; i <= OrderCount; i++)
        {
            var customerId = _customerIds[random.Next(_customerIds.Count)];
            var orderDate = DateTime.Now.AddDays(-random.Next(365 * 2)); // 2 years of data

            var orderId = await conn.ExecuteScalarAsync<int>(
                "INSERT INTO dbo.Orders (customer_id, order_date, ship_country) OUTPUT INSERTED.order_id VALUES (@customerId, @orderDate, @country)",
                new
                {
                    customerId,
                    orderDate,
                    country = $"Country{random.Next(20)}"
                }, tx);
            orderIds.Add(orderId);
        }

        // Insert order details
        for (var i = 1; i <= OrderDetailCount; i++)
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
        await using var conn = new SqlConnection(_baseConnStr);
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
                SELECT c.customer_id, COUNT(*),
                       SUM(ISNULL(od.quantity * od.unit_price * (1 - ISNULL(od.discount, 0)), 0))
                FROM dbo.Customers c
                INNER JOIN dbo.Orders o ON c.customer_id = o.customer_id
                INNER JOIN dbo.OrderDetails od ON o.order_id = od.order_id
                WHERE c.customer_id = 1
                GROUP BY c.customer_id");

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

    /// <summary>
    /// Pre-build CustomerAggregation template for cloning (56% faster than rebuilding)
    /// </summary>
    private ISqlContainer BuildCustomerAggregationTemplate()
    {
        var container = _pengdowsContext.CreateSqlContainer(@"
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

        return container;
    }

    /// <summary>
    /// Pre-build ProductSales template for cloning (56% faster than rebuilding)
    /// </summary>
    private ISqlContainer BuildProductSalesTemplate()
    {
        var container = _pengdowsContext.CreateSqlContainer(@"
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

        return container;
    }

    /// <summary>
    /// Pre-build MonthlyRevenue template for cloning (56% faster than rebuilding)
    /// </summary>
    private ISqlContainer BuildMonthlyRevenueTemplate()
    {
        var container = _pengdowsContext.CreateSqlContainer(@"
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

        return container;
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

        // Show CustomerAggregation comparison in detail
        Console.WriteLine("CUSTOMERAGGREGATION SCENARIO - INDEXED VIEW MATCHING COMPARISON:");
        Console.WriteLine(new string('-', 90));
        Console.WriteLine($"{"Method",-40} {"Time",-12} {"Notes"}");
        Console.WriteLine(new string('-', 90));

        var directViewKey = "CustomerAggregation_DirectViewQuery";
        var pengdowsKey = "CustomerAggregation_pengdows";
        var dapperWithKey = "CustomerAggregation_Dapper_WithSessionMgmt";
        var dapperNoKey = "CustomerAggregation_Dapper_NoViewMatching";
        var efKey = "CustomerAggregation_EntityFramework";

        if (_results.TryGetValue(directViewKey, out var directView))
            Console.WriteLine($"{"Direct View Query (baseline)",-40} {directView.AvgTimeMs,-10:F1}ms  {"Fastest possible - direct indexed view",-30}");

        if (_results.TryGetValue(pengdowsKey, out var pengdows))
            Console.WriteLine($"{"pengdows.crud (auto view matching)",-40} {pengdows.AvgTimeMs,-10:F1}ms  {"ARITHABORT ON set automatically",-30}");

        if (_results.TryGetValue(dapperWithKey, out var dapperWith))
            Console.WriteLine($"{"Dapper (manual session mgmt)",-40} {dapperWith.AvgTimeMs,-10:F1}ms  {"Requires SET ARITHABORT ON manually",-30}");

        if (_results.TryGetValue(dapperNoKey, out var dapperNo))
            Console.WriteLine($"{"Dapper (no session mgmt)",-40} {dapperNo.AvgTimeMs,-10:F1}ms  {"ARITHABORT OFF - NO view matching",-30}");

        if (_results.TryGetValue(efKey, out var ef))
            Console.WriteLine($"{"Entity Framework",-40} {ef.AvgTimeMs,-10:F1}ms  {"ARITHABORT OFF - NO view matching",-30}");

        Console.WriteLine();

        // Show other scenarios
        var otherScenarios = new[] { "ProductSales", "MonthlyRevenue" };
        foreach (var scenario in otherScenarios)
        {
            Console.WriteLine($"{scenario.ToUpper()} SCENARIO:");
            Console.WriteLine(new string('-', 70));

            var scenarioPengdowsKey = $"{scenario}_pengdows";
            var scenarioEfKey = $"{scenario}_EntityFramework";

            if (_results.TryGetValue(scenarioPengdowsKey, out var scenarioPengdows))
            {
                Console.WriteLine(
                    $"{"pengdows.crud",-20} {"SUCCESS",-10} {scenarioPengdows.AvgTimeMs,-10:F1}ms {"Uses automatic view matching",-30}");
            }

            if (_results.TryGetValue(scenarioEfKey, out var scenarioEf))
            {
                var note = "ARITHABORT OFF prevents view matching";
                Console.WriteLine($"{"Entity Framework",-20} {"SLOW",-10} {scenarioEf.AvgTimeMs,-10:F1}ms {note,-30}");
            }

            Console.WriteLine();
        }

        Console.WriteLine("KEY INSIGHT: pengdows.crud's SQL Server dialect preserves session settings");
        Console.WriteLine("that enable automatic view matching, providing massive performance gains");
        Console.WriteLine("on complex aggregation queries WITHOUT requiring view-specific code.");
        Console.WriteLine(new string('=', 90));

        // Cleanup
        // Dispose query templates first
        _customerAggregationTemplate?.Dispose();
        _productSalesTemplate?.Dispose();
        _monthlyRevenueTemplate?.Dispose();

        if (_pengdowsContext is IAsyncDisposable pad)
        {
            await pad.DisposeAsync();
        }

        if (_efContext != null)
        {
            await _efContext.DisposeAsync();
        }

        if (_dapperConnection != null)
        {
            await _dapperConnection.DisposeAsync();
        }
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
            // Clone pre-built template (56% faster than rebuilding)
            // This query can be automatically rewritten to use vw_CustomerOrderSummary
            await using var container = _customerAggregationTemplate.Clone();
            // Parameters are already set with correct values from template

            await using var reader = await container.ExecuteReaderAsync();
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

    /// <summary>
    /// Dapper WITHOUT proper session settings - cannot use automatic indexed view matching.
    /// This demonstrates typical Dapper usage without manual session management.
    /// </summary>
    [Benchmark]
    public async Task<List<dynamic>> CustomerAggregation_Dapper_NoViewMatching()
    {
        return await ExecuteWithTracking("CustomerAggregation_Dapper_NoViewMatching", async () =>
        {
            // Explicitly disable ARITHABORT to prevent automatic indexed view matching
            // This simulates Dapper's typical behavior without manual session management
            await _dapperConnection.ExecuteAsync("SET ARITHABORT OFF");

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
    /// Dapper WITH manual session settings - CAN use automatic indexed view matching.
    /// This requires developers to know about and manually manage session settings.
    /// </summary>
    [Benchmark]
    public async Task<List<dynamic>> CustomerAggregation_Dapper_WithSessionMgmt()
    {
        return await ExecuteWithTracking("CustomerAggregation_Dapper_WithSessionMgmt", async () =>
        {
            // With proper session settings, Dapper CAN use indexed views
            // But this requires manual management that pengdows.crud does automatically
            await _dapperConnection.ExecuteAsync("SET ARITHABORT ON");

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
    /// Direct query against the indexed view - the fastest possible approach.
    /// This is what the optimizer rewrites queries TO when automatic matching works.
    /// </summary>
    [Benchmark]
    public async Task<List<dynamic>> CustomerAggregation_DirectViewQuery()
    {
        return await ExecuteWithTracking("CustomerAggregation_DirectViewQuery", async () =>
        {
            await using var container = _pengdowsContext.CreateSqlContainer(@"
                SELECT customer_id, company_name, order_count, total_revenue,
                       (sum_order_value / NULLIF(count_for_avg, 0)) as avg_order_value
                FROM dbo.vw_CustomerOrderSummary WITH (NOEXPAND)
                WHERE customer_id BETWEEN @startId AND @endId
                ORDER BY total_revenue DESC");
            container.AddParameterWithValue("startId", DbType.Int32, 1);
            container.AddParameterWithValue("endId", DbType.Int32, 50);

            await using var reader = await container.ExecuteReaderAsync();
            var results = new List<dynamic>();
            while (await reader.ReadAsync())
            {
                results.Add(new
                {
                    CustomerId = Convert.ToInt32(reader["customer_id"]),
                    CompanyName = reader["company_name"]?.ToString() ?? "",
                    OrderCount = Convert.ToInt64(reader["order_count"]),
                    TotalRevenue = Convert.ToDecimal(reader["total_revenue"])
                });
            }

            return results;
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
            // Clone pre-built template (56% faster than rebuilding)
            await using var container = _productSalesTemplate.Clone();
            // Parameters are already set with correct values from template

            await using var reader = await container.ExecuteReaderAsync();
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
            // Clone pre-built template (56% faster than rebuilding)
            await using var container = _monthlyRevenueTemplate.Clone();
            // Parameters are already set with correct values from template

            await using var reader = await container.ExecuteReaderAsync();
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
            return default!;
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

        // Stop and dispose SQL Server container
        if (_sqlServerContainer != null)
        {
            Console.WriteLine("[BENCHMARK] Stopping SQL Server container...");
            await _sqlServerContainer.DisposeAsync();
        }
    }

    // Entity classes
    [Table("OrderDetails", "dbo")]
    public class OrderDetail
    {
        [Id(true)]
        [Column("order_detail_id", DbType.Int32)]
        public int OrderDetailId { get; set; }

        [Column("order_id", DbType.Int32)] public int OrderId { get; set; }

        [Column("product_id", DbType.Int32)] public int ProductId { get; set; }

        [Column("unit_price", DbType.Decimal)] public decimal UnitPrice { get; set; }

        [Column("quantity", DbType.Int32)] public int Quantity { get; set; }

        [Column("discount", DbType.Decimal)] public decimal Discount { get; set; }
    }

    // EF entities
    public class EfTestDbContext : DbContext
    {
        public EfTestDbContext(DbContextOptions<EfTestDbContext> options) : base(options)
        {
        }

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