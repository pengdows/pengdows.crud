using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;

namespace CrudBenchmarks;

internal sealed class IndexedViewEnvironment : IAsyncDisposable
{
    private const string Password = "YourStrong@Passw0rd";
    private const string Database = "IndexedViewBenchmark";
    private const string Schema = "dbo";
    private const string ViewName = "vw_CustomerOrderSummary";
    private const int DefaultCustomerCount = 1000;
    private const int DefaultOrdersPerCustomer = 10;

    private static readonly string ViewTemplate = $$"""
        SELECT customer_id as CustomerId,
               order_count as OrderCount,
               total_amount as TotalAmount,
               sum_order_amount as SumOrderAmount,
               count_for_avg as CountForAvg
        FROM {{Schema}}.{{ViewName}} WITH (NOEXPAND)
        WHERE customer_id = {customerId}
        """;

    private static readonly string BaseAggregateTemplate = $$"""
        SELECT customer_id as CustomerId,
               COUNT_BIG(*) as OrderCount,
               SUM(total_amount) as TotalAmount,
               SUM(total_amount) as SumOrderAmount,
               COUNT_BIG(*) as CountForAvg
        FROM {{Schema}}.Orders
        WHERE status = 'Active' AND customer_id = {customerId}
        GROUP BY customer_id
        """;

    private readonly List<int> _customerIds = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IContainer? _sqlServerContainer;
    private bool _initialized;
    private string _connectionString = string.Empty;
    private string _baseConnectionString = string.Empty;
    private string _viewClusteredIndex = string.Empty;

    public string ConnectionString => _connectionString;

    public string GetConnectionStringWithApplicationName(string appName)
    {
        return string.IsNullOrWhiteSpace(appName)
            ? _connectionString
            : _connectionString + "Application Name=" + appName + ";";
    }

    public IReadOnlyList<int> CustomerIds => _customerIds;

    public int SampleCustomerId => _customerIds.Count > 0
        ? _customerIds[0]
        : throw new InvalidOperationException("No customer IDs are available. Ensure InitializeAsync has run.");

    public string ViewFullName => $"{Schema}.{ViewName}";

    public string ViewClusteredIndexName => _viewClusteredIndex;

    public async Task InitializeAsync(int customerCount = DefaultCustomerCount, int ordersPerCustomer = DefaultOrdersPerCustomer)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            await StartSqlServerAsync();
            await EnsureDatabaseAsync();
            await SeedDataAsync(customerCount, ordersPerCustomer);
            await CaptureViewIndexNameAsync();

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task StartSqlServerAsync()
    {
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
        _baseConnectionString = $"Server=localhost,{hostPort};Database={Database};User Id=sa;Password={Password};TrustServerCertificate=true;Connection Timeout=30;";
        _connectionString = _baseConnectionString;

        Console.WriteLine($"[BENCHMARK] SQL Server container listening on port {hostPort}");

        await WaitForSqlServerAsync();
    }

    private async Task WaitForSqlServerAsync()
    {
        var masterConnStr = _baseConnectionString.Replace($"Database={Database}", "Database=master");
        Console.WriteLine("[BENCHMARK] Waiting for SQL Server readiness...");
        for (var i = 0; i < 120; i++)
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

        throw new TimeoutException("SQL Server did not signal readiness within the allotted time.");
    }

    private async Task EnsureDatabaseAsync()
    {
        var masterConnStr = _baseConnectionString.Replace($"Database={Database}", "Database=master");
        await using var masterConn = new SqlConnection(masterConnStr);
        await masterConn.OpenAsync();

        await masterConn.ExecuteAsync($@"
            IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{Database}')
            BEGIN
                CREATE DATABASE [{Database}];
            END");

        await masterConn.CloseAsync();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
            IF OBJECT_ID('dbo.vw_CustomerOrderSummary', 'V') IS NOT NULL
                DROP VIEW dbo.vw_CustomerOrderSummary;
            IF OBJECT_ID('dbo.Orders', 'U') IS NOT NULL
                DROP TABLE dbo.Orders;
            IF OBJECT_ID('dbo.Customers', 'U') IS NOT NULL
                DROP TABLE dbo.Customers;");

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
            );
        ");

        await conn.ExecuteAsync($@"
            CREATE VIEW dbo.vw_CustomerOrderSummary WITH SCHEMABINDING AS
            SELECT
                customer_id,
                COUNT_BIG(*) AS order_count,
                SUM(total_amount) AS total_amount,
                SUM(total_amount) AS sum_order_amount,
                COUNT_BIG(*) AS count_for_avg
            FROM dbo.Orders
            WHERE status = 'Active'
            GROUP BY customer_id;

            CREATE UNIQUE CLUSTERED INDEX IX_CustomerOrderSummary_CustomerID
                ON dbo.vw_CustomerOrderSummary(customer_id);

            CREATE INDEX IX_Orders_CustomerID_Status ON dbo.Orders(customer_id, status);
        ");
    }

    private async Task SeedDataAsync(int customerCount, int ordersPerCustomer)
    {
        _customerIds.Clear();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        for (var i = 1; i <= customerCount; i++)
        {
            var customerId = await conn.ExecuteScalarAsync<int>(
                "INSERT INTO dbo.Customers (company_name) OUTPUT INSERTED.customer_id VALUES (@name)",
                new { name = $"Company {i:D6}" }, tx);
            _customerIds.Add(customerId);
        }

        var random = new Random(42);
        foreach (var customerId in _customerIds)
        {
            var orderCount = Math.Max(1, random.Next(ordersPerCustomer / 2, ordersPerCustomer * 2));
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
        await conn.ExecuteAsync(@"
            UPDATE STATISTICS dbo.Customers;
            UPDATE STATISTICS dbo.Orders;
            UPDATE STATISTICS dbo.vw_CustomerOrderSummary;
        ");
    }

    private async Task CaptureViewIndexNameAsync()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        _viewClusteredIndex = await conn.ExecuteScalarAsync<string>(@"
            SELECT i.name
            FROM sys.indexes i
            JOIN sys.views v ON i.object_id = v.object_id
            WHERE SCHEMA_NAME(v.schema_id) = @schema
              AND v.name = @view
              AND i.type_desc = 'CLUSTERED'
              AND i.is_unique = 1",
            new { schema = Schema, view = ViewName }) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_viewClusteredIndex))
        {
            throw new InvalidOperationException("Failed to detect the clustered index on the indexed view.");
        }
    }

    public string BuildViewSql(Func<string, string> param) =>
        ViewTemplate.Replace("{customerId}", param("customerId"));

    public string BuildBaseAggregateSql(Func<string, string> param) =>
        BaseAggregateTemplate.Replace("{customerId}", param("customerId"));

    public async ValueTask DisposeAsync()
    {
        if (_sqlServerContainer != null)
        {
            Console.WriteLine("[BENCHMARK] Stopping SQL Server container...");
            await _sqlServerContainer.DisposeAsync();
            _sqlServerContainer = null;
        }
    }
}
