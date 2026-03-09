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
using pengdows.crud.infrastructure;
using pengdows.stormgate;
using SqlKata.Compilers;

namespace CrudBenchmarks;

/// <summary>
/// Thesis point #3 (continued): PostgreSQL materialized view benchmark.
/// Compares materialized-view look-ups (pre-computed) against live table-scan
/// GROUP BY queries across pengdows.crud, Dapper, and Entity Framework.
/// </summary>
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5, invocationCount: 50)]
public class MaterializedViewPerformanceBenchmarks : IAsyncDisposable
{
    private const string TableScanSqlTemplate = """
                                                SELECT
                                                    customer_id as customer_id,
                                                    COUNT(*) as order_count,
                                                    SUM(total_amount) as total_amount,
                                                    AVG(total_amount) as avg_order_amount,
                                                    MAX(order_date) as last_order_date
                                                FROM orders
                                                WHERE customer_id = {customerId} AND status = {status}
                                                GROUP BY customer_id
                                                """;

    private const string MaterializedViewSqlTemplate = """
                                                       SELECT
                                                           customer_id as customer_id,
                                                           order_count as order_count,
                                                           total_amount as total_amount,
                                                           avg_order_amount as avg_order_amount,
                                                           last_order_date as last_order_date
                                                       FROM customer_order_summary
                                                       WHERE customer_id = {customerId}
                                                       """;

    private IContainer? _container;
    private string _connStr = string.Empty;

    // Singletons — one instance for the lifetime of the benchmark run
    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private TableGateway<PgCustomerOrderSummary, int> _summaryHelper = null!;
    private MatViewEfContext _efContext = null!;
    private DbContextOptions<MatViewEfContext> _efOptions = null!;
    private NpgsqlDataSource _dapperDataSource = null!;
    private StormGate _stormGate = null!;
    private PostgresCompiler _sqlKataCompiler = null!;

    // Pre-built containers for the PreBuilt variants (SQL building excluded from timing)
    private ISqlContainer? _matViewSc;
    private ISqlContainer? _tableScanSc;

    // Pre-built raw SQL strings for Dapper (avoids string.Replace in timed region)
    private string _matViewDapperSql = null!;
    private string _tableScanDapperSql = null!;

    private readonly List<int> _customerIds = new();
    private int _testCustomerId;

    [Params(2000, 5000)] public int CustomerCount;

    private const int OrdersPerCustomer = 15;
    private const int Parallelism = 128;
    private const int OperationsPerRun = 512;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithEnvironment("POSTGRES_DB", "matview_perf_test")
            .WithPortBinding(0, 5432)
            // Raise max_connections so multiple independent pools (pengdows, Dapper, EF, StormGate)
            // can each hold up to 100 connections without competing with postgres's default limit of 100.
            .WithCommand("-c", "max_connections=300")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _container.StartAsync();

        var mappedPort = _container.GetMappedPublicPort(5432);
        _connStr =
            $"Host=localhost;Port={mappedPort};Database=matview_perf_test;Username=postgres;Password=postgres";

        // Create the shared data source first so WaitForReady and seeding use the same
        // pool as the Dapper/StormGate benchmarks — no orphan connections in the global pool.
        _dapperDataSource = NpgsqlDataSource.Create(_connStr);

        await WaitForReady();
        await CreateSchemaAndSeedAsync();

        // Setup pengdows.crud
        _map = new TypeMapRegistry();
        _map.Register<PgCustomerOrderSummary>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _connStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsContext = new DatabaseContext(cfg, NpgsqlFactory.Instance, null, _map);
        _summaryHelper = new TableGateway<PgCustomerOrderSummary, int>(_pengdowsContext);

        // Setup Entity Framework
        _efOptions = new DbContextOptionsBuilder<MatViewEfContext>()
            .UseNpgsql(_connStr)
            .Options;
        _efContext = new MatViewEfContext(_efOptions);

        // SQL Kata compiler singleton
        _sqlKataCompiler = new PostgresCompiler();

        // StormGate: connection rate governor wrapping the same data source
        // 100 permits matches the default Npgsql/ADO.NET connection pool max size
        _stormGate = new StormGate(_dapperDataSource, 100, TimeSpan.FromSeconds(5));

        _testCustomerId = _customerIds[0];

        // Pre-built containers (SQL building excluded from timed region)
        // MaterializedView — BuildBaseRetrieve generates SELECT from [Column] attributes
        _matViewSc = _summaryHelper.BuildBaseRetrieve("mv");
        _matViewSc.Query.Append(" WHERE ");
        _matViewSc.Query.Append(_matViewSc.WrapObjectName("mv.customer_id"));
        _matViewSc.Query.Append(" = ");
        var mvParam = _matViewSc.AddParameterWithValue(DbType.Int32, _testCustomerId);
        _matViewSc.Query.Append(_matViewSc.MakeParameterName(mvParam));

        // TableScan — raw SQL appropriate for GROUP BY aggregates
        _tableScanSc = _pengdowsContext.CreateSqlContainer();
        _tableScanSc.Query.Append(BuildTableScanSql(p => _tableScanSc.MakeParameterName(p)));
        _tableScanSc.AddParameterWithValue("customerId", DbType.Int32, _testCustomerId);
        _tableScanSc.AddParameterWithValue("status", DbType.String, "Active");

        // Pre-built raw SQL strings for Dapper (constant — avoids string.Replace in timed region)
        _matViewDapperSql = BuildViewSql(p => $"@{p}");
        _tableScanDapperSql = BuildTableScanSql(p => $"@{p}");

        Console.WriteLine(
            $"[BENCHMARK] MaterializedViewPerformance: {CustomerCount} customers, {OrdersPerCustomer} orders/customer");
        Console.WriteLine($"[BENCHMARK] pengdows.crud ConnectionMode: {_pengdowsContext.ConnectionMode}");
    }

    private async Task WaitForReady()
    {
        for (var i = 0; i < 60; i++)
        {
            try
            {
                await using var conn = await _dapperDataSource.OpenConnectionAsync();
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
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync(@"
            DROP MATERIALIZED VIEW IF EXISTS customer_order_summary;
            DROP TABLE IF EXISTS orders;
            DROP TABLE IF EXISTS customers;

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

            CREATE INDEX idx_orders_customer_id_status ON orders(customer_id, status);
            ", transaction: tx);

        // Seed customers
        for (var i = 1; i <= CustomerCount; i++)
        {
            var customerId = await conn.ExecuteScalarAsync<int>(
                "INSERT INTO customers (company_name) VALUES (@name) RETURNING customer_id",
                new { name = $"Company {i:D6}" }, tx);
            _customerIds.Add(customerId);
        }

        // Seed orders
        var random = new Random(42);
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

        // Create materialized view
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

            CREATE UNIQUE INDEX idx_customer_order_summary_cid
            ON customer_order_summary(customer_id);
            ", transaction: tx);

        await tx.CommitAsync();

        await conn.ExecuteAsync(@"
            ANALYZE customers;
            ANALYZE orders;
            ANALYZE customer_order_summary;
        ");
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _matViewSc?.Dispose();
        _tableScanSc?.Dispose();

        if (_pengdowsContext is IAsyncDisposable pad)
        {
            await pad.DisposeAsync();
        }

        if (_efContext != null)
        {
            await _efContext.DisposeAsync();
        }

        _stormGate?.Dispose();

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

    // ── Materialized view: pre-built (SQL building excluded from timing) ─

    /// <summary>Pre-built container — measures pure execution cost only.</summary>
    [Benchmark]
    public async Task<PgCustomerOrderSummary?> MatView_Pengdows_PreBuilt()
    {
        return await _summaryHelper.LoadSingleAsync(_matViewSc!);
    }

    /// <summary>Pre-built SQL string — measures pure execution cost only.</summary>
    [Benchmark]
    public async Task<PgCustomerOrderSummary?> MatView_Dapper_PreBuilt()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<PgCustomerOrderSummary>(
            _matViewDapperSql,
            new { customerId = _testCustomerId });
    }

    // ── Materialized view: build + run (level set — fair SQL-builder comparison) ─

    /// <summary>
    /// Builds query via TableGateway (BuildBaseRetrieve + WHERE) then executes.
    /// Represents real developer usage of pengdows.crud.
    /// </summary>
    [Benchmark]
    public async Task<PgCustomerOrderSummary?> MatView_Pengdows_BuildAndRun()
    {
        await using var sc = _summaryHelper.BuildBaseRetrieve("mv");
        sc.Query.Append(" WHERE ");
        sc.Query.Append(sc.WrapObjectName("mv.customer_id"));
        sc.Query.Append(" = ");
        var p = sc.AddParameterWithValue(DbType.Int32, _testCustomerId);
        sc.Query.Append(sc.MakeParameterName(p));
        return await _summaryHelper.LoadSingleAsync(sc);
    }

    /// <summary>
    /// Builds query via SQL Kata (singleton compiler) then executes with Dapper.
    /// Represents real developer usage of Dapper with a query builder.
    /// </summary>
    [Benchmark]
    public async Task<PgCustomerOrderSummary?> MatView_Dapper_SqlKata()
    {
        var q = new SqlKata.Query("customer_order_summary")
            .Where("customer_id", _testCustomerId)
            .Select("customer_id", "order_count", "total_amount", "avg_order_amount", "last_order_date");
        var compiled = _sqlKataCompiler.Compile(q);
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<PgCustomerOrderSummary>(
            compiled.Sql,
            compiled.NamedBindings);
    }

    /// <summary>
    /// Dapper with StormGate connection governor — pre-built SQL, governed connection acquire.
    /// Represents a DIY connection-governance wrapper on top of Dapper.
    /// </summary>
    [Benchmark]
    public async Task<PgCustomerOrderSummary?> MatView_Dapper_StormGate()
    {
        await using var conn = await _stormGate.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<PgCustomerOrderSummary>(
            _matViewDapperSql,
            new { customerId = _testCustomerId });
    }

    [Benchmark]
    public async Task<CustomerOrderSummaryRow?> MatView_EntityFramework()
    {
        return await _efContext.CustomerOrderSummaryRows
            .FromSqlRaw(_matViewDapperSql, new NpgsqlParameter("customerId", _testCustomerId))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    // ── Table scan: pre-built (SQL building excluded from timing) ────────

    [Benchmark]
    public async Task<PgCustomerOrderSummary?> TableScan_Pengdows_PreBuilt()
    {
        return await _summaryHelper.LoadSingleAsync(_tableScanSc!);
    }

    [Benchmark]
    public async Task<PgCustomerOrderSummary?> TableScan_Dapper_PreBuilt()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<PgCustomerOrderSummary>(
            _tableScanDapperSql,
            new { customerId = _testCustomerId, status = "Active" });
    }

    [Benchmark]
    public async Task<CustomerOrderSummaryRow?> TableScan_EntityFramework()
    {
        return await _efContext.CustomerOrderSummaryRows
            .FromSqlRaw(
                _tableScanDapperSql,
                new NpgsqlParameter("customerId", _testCustomerId),
                new NpgsqlParameter("status", "Active"))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    // ── Concurrent: materialized view ────────────────────────────────────

    [Benchmark]
    public async Task MatView_Pengdows_BuildAndRun_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var sc = _summaryHelper.BuildBaseRetrieve("mv");
            sc.Query.Append(" WHERE ");
            sc.Query.Append(sc.WrapObjectName("mv.customer_id"));
            sc.Query.Append(" = ");
            var p = sc.AddParameterWithValue(DbType.Int32, _testCustomerId);
            sc.Query.Append(sc.MakeParameterName(p));
            await _summaryHelper.LoadSingleAsync(sc);
        });
    }

    [Benchmark]
    public async Task MatView_Dapper_SqlKata_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            var q = new SqlKata.Query("customer_order_summary")
                .Where("customer_id", _testCustomerId)
                .Select("customer_id", "order_count", "total_amount", "avg_order_amount", "last_order_date");
            var compiled = _sqlKataCompiler.Compile(q);
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            await conn.QuerySingleOrDefaultAsync<PgCustomerOrderSummary>(
                compiled.Sql,
                compiled.NamedBindings);
        });
    }

    [Benchmark]
    public async Task MatView_Dapper_StormGate_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = await _stormGate.OpenAsync();
            await conn.QuerySingleOrDefaultAsync<PgCustomerOrderSummary>(
                _matViewDapperSql,
                new { customerId = _testCustomerId });
        });
    }

    [Benchmark]
    public async Task MatView_EntityFramework_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new MatViewEfContext(_efOptions);
            await ctx.CustomerOrderSummaryRows
                .FromSqlRaw(_matViewDapperSql, new NpgsqlParameter("customerId", _testCustomerId))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string BuildTableScanSql(Func<string, string> param)
    {
        return TableScanSqlTemplate
            .Replace("{customerId}", param("customerId"))
            .Replace("{status}", param("status"));
    }

    private static string BuildViewSql(Func<string, string> param)
    {
        return MaterializedViewSqlTemplate.Replace("{customerId}", param("customerId"));
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
    }

    // ── pengdows.crud entity ────────────────────────────────────────────

    [Table("customer_order_summary")]
    public class PgCustomerOrderSummary
    {
        [Id(false)]
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

    // ── EF Context ──────────────────────────────────────────────────────

    public class MatViewEfContext : DbContext
    {
        public MatViewEfContext(DbContextOptions<MatViewEfContext> options) : base(options)
        {
        }

        public DbSet<EfCustomer> Customers { get; set; }
        public DbSet<EfOrder> Orders { get; set; }
        public DbSet<CustomerOrderSummaryRow> CustomerOrderSummaryRows { get; set; }

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

            modelBuilder.Entity<CustomerOrderSummaryRow>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
                entity.Property(e => e.CustomerId).HasColumnName("customer_id");
                entity.Property(e => e.OrderCount).HasColumnName("order_count");
                entity.Property(e => e.TotalAmount).HasColumnName("total_amount");
                entity.Property(e => e.AvgOrderAmount).HasColumnName("avg_order_amount");
                entity.Property(e => e.LastOrderDate).HasColumnName("last_order_date");
            });
        }
    }

    public class EfCustomer
    {
        public int CustomerId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
    }

    public class EfOrder
    {
        public int OrderId { get; set; }
        public int CustomerId { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "Active";
    }

    [Keyless]
    public class CustomerOrderSummaryRow
    {
        public int CustomerId { get; set; }
        public long OrderCount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AvgOrderAmount { get; set; }
        public DateTime LastOrderDate { get; set; }
    }
}
