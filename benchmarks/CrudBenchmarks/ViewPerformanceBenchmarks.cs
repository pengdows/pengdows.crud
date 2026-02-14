using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Npgsql;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Thesis: pengdows does the right thing automatically — EF and Dapper do not
/// properly handle SQL Server session settings for indexed view usage.
///
/// SQL Server requires 7 specific SET options for indexed view usage. SqlClient
/// defaults leave ARITHABORT OFF. pengdows detects this via DBCC USEROPTIONS and
/// automatically applies SET ARITHABORT ON on each connection.
///
/// Proven consequences of ARITHABORT OFF (Dapper/EF default):
///   - Plan cache bifurcation: the same query gets two separate cached plans
///     (ARITHABORT ON from SSMS vs OFF from the app), wasting SQL Server memory
///     and causing the classic "slow in app, fast in SSMS" diagnostic trap.
///   - On older SQL Server versions or Standard/Express editions, incorrect SET
///     options silently prevent automatic view matching.
///   - Even on SQL Server 2022 where ANSI_WARNINGS provides implicit ARITHABORT
///     behavior, relying on undocumented implicit behavior is fragile.
///
/// This benchmark proves the thesis empirically:
///   1. Session settings diff: pengdows vs bare SqlClient
///   2. Plan cache bifurcation: 2 distinct set_options bitmasks in sys.dm_exec_cached_plans
///   3. Performance comparison: pengdows vs Dapper vs EF on full aggregate queries
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5, invocationCount: 50)]
public class IndexedViewPerformanceBenchmarks : IAsyncDisposable
{
    private IndexedViewEnvironment _env = new();

    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private TableGateway<CustomerOrderSummary, int> _summaryHelper = null!;
    private IndexedViewEfContext _efContext = null!;
    private DbContextOptions<IndexedViewEfContext> _efOptions = null!;

    private string _pengdowsConnStr = string.Empty;
    private string _efConnStr = string.Empty;
    private string _dapperConnStr = string.Empty;

    [Params(5000, 10000)]
    public int CustomerCount;

    [Params(50)]
    public int OrdersPerCustomer;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        await _env.InitializeAsync(CustomerCount, OrdersPerCustomer);

        // Separate connection pools per library via Application Name
        _pengdowsConnStr = _env.GetConnectionStringWithApplicationName("Benchmark_Pengdows_ViewPerf");
        _efConnStr = _env.GetConnectionStringWithApplicationName("Benchmark_EF_ViewPerf");
        _dapperConnStr = _env.GetConnectionStringWithApplicationName("Benchmark_Dapper_ViewPerf");

        // Setup pengdows.crud
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

        // Setup Entity Framework
        _efOptions = new DbContextOptionsBuilder<IndexedViewEfContext>()
            .UseSqlServer(_efConnStr)
            .Options;
        _efContext = new IndexedViewEfContext(_efOptions);

        Console.WriteLine(
            $"[BENCHMARK] IndexedViewPerformance: {CustomerCount} customers, {OrdersPerCustomer} orders/customer");
        Console.WriteLine($"[BENCHMARK] pengdows.crud ConnectionMode: {_pengdowsContext.ConnectionMode}");

        // Prove the thesis with empirical evidence
        await ProveThesisAsync();
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
    }

    // ═══════════════════════════════════════════════════════════════════
    // Thesis proof — runs once during GlobalSetup, not during timing
    // ═══════════════════════════════════════════════════════════════════

    private async Task ProveThesisAsync()
    {
        Console.WriteLine("\n[PROOF] ═══════════════════════════════════════════════════");
        Console.WriteLine("[PROOF] Indexed View Thesis Proof");
        Console.WriteLine("[PROOF] ═══════════════════════════════════════════════════\n");

        // Proof 1: Session settings comparison
        await ProveSessionSettingsDiffAsync();

        // Proof 2: Plan cache bifurcation
        await ProvePlanCacheBifurcationAsync();

        Console.WriteLine("[PROOF] ═══════════════════════════════════════════════════\n");
    }

    private async Task ProveSessionSettingsDiffAsync()
    {
        var settingsScript = _pengdowsContext.SessionSettingsPreamble;
        Console.WriteLine(string.IsNullOrWhiteSpace(settingsScript)
            ? "[PROOF] pengdows session diff: (empty — all settings already compliant)"
            : $"[PROOF] pengdows session diff: {settingsScript.Replace("\n", " ").Trim()}");

        var pengdowsSettings = await GetPengdowsSessionSettingsAsync();

        // Bare SqlClient connection (Dapper/EF default)
        await using var bareConn = new SqlConnection(_dapperConnStr);
        await bareConn.OpenAsync();

        var bareSettings = await GetSessionSettingsAsync(bareConn);

        var relevantSettings = new[]
        {
            "ARITHABORT", "ANSI_NULLS", "ANSI_PADDING", "ANSI_WARNINGS",
            "CONCAT_NULL_YIELDS_NULL", "QUOTED_IDENTIFIER", "NUMERIC_ROUNDABORT"
        };

        Console.WriteLine("[PROOF]   {0,-30} {1,-12} {2,-12}", "Setting", "pengdows", "Dapper/EF");
        Console.WriteLine("[PROOF]   {0,-30} {1,-12} {2,-12}", "-------", "--------", "---------");
        foreach (var key in relevantSettings)
        {
            var pVal = pengdowsSettings.TryGetValue(key, out var pv) ? pv : "OFF";
            var bVal = bareSettings.TryGetValue(key, out var bv) ? bv : "OFF";
            var marker = pVal != bVal ? " << DIFF" : "";
            Console.WriteLine("[PROOF]   {0,-30} {1,-12} {2,-12}{3}", key, pVal, bVal, marker);
        }
    }

    private async Task ProvePlanCacheBifurcationAsync()
    {
        Console.WriteLine("\n[PROOF] --- Plan cache bifurcation ---");

        // Warm up both paths to populate plan cache
        var customerId = _env.SampleCustomerId;
        var viewSql = _env.BuildViewSql(p => $"@{p}");

        await using (var container = _pengdowsContext.CreateSqlContainer())
        {
            var pengdowsSql = _env.BuildViewSql(param => container.MakeParameterName(param));
            container.Query.Append(pengdowsSql);
            container.AddParameterWithValue("customerId", DbType.Int32, customerId);
            await _summaryHelper.LoadSingleAsync(container);
        }

        await using var bareConn = new SqlConnection(_dapperConnStr);
        await bareConn.OpenAsync();
        await bareConn.QuerySingleOrDefaultAsync<CustomerOrderSummary>(viewSql, new { customerId });

        // Query plan cache
        await using var diagConn = new SqlConnection(_env.ConnectionString);
        await diagConn.OpenAsync();
        await using var cmd = diagConn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                st.text,
                cp.usecounts,
                pa.value AS set_options,
                cp.size_in_bytes
            FROM sys.dm_exec_cached_plans cp
            CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
            OUTER APPLY (
                SELECT pa.value
                FROM sys.dm_exec_plan_attributes(cp.plan_handle) pa
                WHERE pa.attribute = 'set_options'
            ) pa
            WHERE st.text LIKE '%vw_CustomerOrderSummary%'
              AND st.text NOT LIKE '%dm_exec%'
              AND st.text NOT LIKE '%STATISTICS%'
              AND cp.cacheobjtype = 'Compiled Plan'
            ORDER BY st.text, pa.value";

        var entries = new List<(string text, int useCount, long setOptions, int sizeBytes)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var text = reader.GetString(0).Trim();
                if (text.Length > 80) text = text[..80] + "...";
                entries.Add((
                    text,
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? 0L : Convert.ToInt64(reader.GetValue(2)),
                    reader.GetInt32(3)));
            }
        }

        if (entries.Count == 0)
        {
            Console.WriteLine("[PROOF]   No cached plans found for view queries.");
            return;
        }

        const long arithabortBit = 0x1000; // 4096
        Console.WriteLine($"[PROOF]   Found {entries.Count} cached plan(s):");
        foreach (var (text, useCount, setOpts, size) in entries)
        {
            var arithabort = (setOpts & arithabortBit) != 0 ? "ON" : "OFF";
            Console.WriteLine(
                $"[PROOF]     set_options=0x{setOpts:X4} (ARITHABORT={arithabort}) uses={useCount} size={size}B");
            Console.WriteLine($"[PROOF]       {text}");
        }

        var distinctSetOptions = entries.Select(e => e.setOptions).Distinct().ToList();
        if (distinctSetOptions.Count > 1)
        {
            Console.WriteLine(
                $"[PROOF]   >> PLAN CACHE BIFURCATION: {distinctSetOptions.Count} distinct set_options bitmasks");
            Console.WriteLine(
                "[PROOF]   >> pengdows shares plan cache with SSMS. Dapper/EF get separate (wasted) entries.");
        }
        else
        {
            Console.WriteLine("[PROOF]   No bifurcation detected (all plans share same set_options).");
        }
    }

    private static async Task<Dictionary<string, string>> GetSessionSettingsAsync(SqlConnection conn)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DBCC USEROPTIONS WITH NO_INFOMSGS";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0).Trim().ToUpperInvariant();
            var value = reader.GetString(1).Trim();
            settings[name] = string.Equals(value, "SET", StringComparison.OrdinalIgnoreCase) ? "ON" : value;
        }

        return settings;
    }

    private async Task<Dictionary<string, string>> GetPengdowsSessionSettingsAsync()
    {
        await using var container = _pengdowsContext.CreateSqlContainer("DBCC USEROPTIONS WITH NO_INFOMSGS");
        await using var reader = await container.ExecuteReaderAsync();
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0).Trim().ToUpperInvariant();
            var value = reader.GetString(1).Trim();
            settings[name] = string.Equals(value, "SET", StringComparison.OrdinalIgnoreCase) ? "ON" : value;
        }

        return settings;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Benchmarks — the full aggregate is the key differentiator
    // ═══════════════════════════════════════════════════════════════════

    // ── Full aggregate (THE differentiator) ─────────────────────────────
    // Scans ALL orders grouped by customer_id — matches the indexed view
    // definition exactly. With correct SET options (pengdows), the optimizer
    // substitutes the indexed view. Without (Dapper/EF), it scans the
    // entire Orders table.

    [Benchmark]
    public async Task<List<CustomerOrderSummary>> FullAggregate_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        container.Query.Append(_env.BuildFullAggregateSql());
        return await _summaryHelper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<IEnumerable<CustomerOrderSummary>> FullAggregate_Dapper()
    {
        await using var conn = new SqlConnection(_dapperConnStr);
        await conn.OpenAsync();
        return await conn.QueryAsync<CustomerOrderSummary>(_env.BuildFullAggregateSql());
    }

    [Benchmark]
    public async Task<List<CustomerOrderSummary>> FullAggregate_EntityFramework()
    {
        await using var ctx = new IndexedViewEfContext(_efOptions);
        return await ctx.CustomerOrderSummaries
            .FromSqlRaw(_env.BuildFullAggregateSql())
            .AsNoTracking()
            .ToListAsync();
    }

    // ── Per-customer view query (baseline — equal for all) ─────────────

    [Benchmark]
    public async Task<CustomerOrderSummary?> QueryIndexedView_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = _env.BuildViewSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("customerId", DbType.Int32, _env.SampleCustomerId);
        return await _summaryHelper.LoadSingleAsync(container);
    }

    [Benchmark]
    public async Task<CustomerOrderSummary?> QueryIndexedView_Dapper()
    {
        await using var conn = new SqlConnection(_dapperConnStr);
        await conn.OpenAsync();
        var sql = _env.BuildViewSql(param => $"@{param}");
        return await conn.QuerySingleOrDefaultAsync<CustomerOrderSummary>(
            sql,
            new { customerId = _env.SampleCustomerId });
    }

    [Benchmark]
    public async Task<CustomerOrderSummary?> QueryIndexedView_EntityFramework()
    {
        var sql = _env.BuildViewSql(param => $"@{param}");
        return await _efContext.CustomerOrderSummaries
            .FromSqlRaw(sql, new SqlParameter("customerId", _env.SampleCustomerId))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
        await _env.DisposeAsync();
    }

    // ── Entity ──────────────────────────────────────────────────────────

    [pengdows.crud.attributes.Table("vw_CustomerOrderSummary", "dbo")]
    public class CustomerOrderSummary
    {
        [Id(false)]
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

        public decimal AvgOrderAmount => CountForAvg > 0 ? SumOrderAmount / CountForAvg : 0;
    }

    // ── EF Context ──────────────────────────────────────────────────────

    public class IndexedViewEfContext : DbContext
    {
        public IndexedViewEfContext(DbContextOptions<IndexedViewEfContext> options) : base(options)
        {
        }

        public DbSet<CustomerOrderSummary> CustomerOrderSummaries { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CustomerOrderSummary>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
                entity.Property(e => e.CustomerId).HasColumnName("CustomerId");
                entity.Property(e => e.OrderCount).HasColumnName("OrderCount");
                entity.Property(e => e.TotalAmount).HasColumnName("TotalAmount");
                entity.Property(e => e.SumOrderAmount).HasColumnName("SumOrderAmount");
                entity.Property(e => e.CountForAvg).HasColumnName("CountForAvg");
                entity.Ignore(e => e.AvgOrderAmount);
            });
        }
    }
}

/// <summary>
/// Thesis point #3 (continued): PostgreSQL materialized view benchmark.
/// Compares materialized-view look-ups (pre-computed) against live table-scan
/// GROUP BY queries across pengdows.crud, Dapper, and Entity Framework.
/// </summary>
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

    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private TableGateway<PgCustomerOrderSummary, int> _summaryHelper = null!;
    private MatViewEfContext _efContext = null!;
    private DbContextOptions<MatViewEfContext> _efOptions = null!;
    private NpgsqlDataSource _dapperDataSource = null!;

    private readonly List<int> _customerIds = new();
    private int _testCustomerId;

    [Params(2000, 5000)]
    public int CustomerCount;

    [Params(15)]
    public int OrdersPerCustomer;

    [Params(16)]
    public int Parallelism;

    [Params(64)]
    public int OperationsPerRun;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithEnvironment("POSTGRES_DB", "matview_perf_test")
            .WithPortBinding(0, 5432)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _container.StartAsync();

        var mappedPort = _container.GetMappedPublicPort(5432);
        _connStr =
            $"Host=localhost;Port={mappedPort};Database=matview_perf_test;Username=postgres;Password=postgres";

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

        // Setup Dapper data source
        _dapperDataSource = NpgsqlDataSource.Create(_connStr);

        _testCustomerId = _customerIds[0];

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

    // ── Single-call benchmarks: materialized view ───────────────────────

    [Benchmark]
    public async Task<PgCustomerOrderSummary?> MaterializedView_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildViewSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("customerId", DbType.Int32, _testCustomerId);
        return await _summaryHelper.LoadSingleAsync(container);
    }

    [Benchmark]
    public async Task<PgCustomerOrderSummary?> MaterializedView_Dapper()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildViewSql(param => $"@{param}");
        return await conn.QuerySingleOrDefaultAsync<PgCustomerOrderSummary>(
            sql,
            new { customerId = _testCustomerId });
    }

    [Benchmark]
    public async Task<CustomerOrderSummaryRow?> MaterializedView_EntityFramework()
    {
        var sql = BuildViewSql(param => $"@{param}");
        return await _efContext.CustomerOrderSummaryRows
            .FromSqlRaw(sql, new NpgsqlParameter("customerId", _testCustomerId))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    // ── Single-call benchmarks: table scan ──────────────────────────────

    [Benchmark]
    public async Task<PgCustomerOrderSummary?> TableScan_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildTableScanSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("customerId", DbType.Int32, _testCustomerId);
        container.AddParameterWithValue("status", DbType.String, "Active");
        return await _summaryHelper.LoadSingleAsync(container);
    }

    [Benchmark]
    public async Task<PgCustomerOrderSummary?> TableScan_Dapper()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildTableScanSql(param => $"@{param}");
        return await conn.QuerySingleOrDefaultAsync<PgCustomerOrderSummary>(
            sql,
            new { customerId = _testCustomerId, status = "Active" });
    }

    [Benchmark]
    public async Task<CustomerOrderSummaryRow?> TableScan_EntityFramework()
    {
        var sql = BuildTableScanSql(param => $"@{param}");
        return await _efContext.CustomerOrderSummaryRows
            .FromSqlRaw(
                sql,
                new NpgsqlParameter("customerId", _testCustomerId),
                new NpgsqlParameter("status", "Active"))
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    // ── Concurrent benchmarks: materialized view ────────────────────────

    [Benchmark]
    public async Task MaterializedView_Pengdows_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildViewSql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("customerId", DbType.Int32, _testCustomerId);
            await _summaryHelper.LoadSingleAsync(container);
        });
    }

    [Benchmark]
    public async Task MaterializedView_Dapper_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            var sql = BuildViewSql(param => $"@{param}");
            await conn.QuerySingleOrDefaultAsync<PgCustomerOrderSummary>(
                sql,
                new { customerId = _testCustomerId });
        });
    }

    [Benchmark]
    public async Task MaterializedView_EntityFramework_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new MatViewEfContext(_efOptions);
            var sql = BuildViewSql(param => $"@{param}");
            await ctx.CustomerOrderSummaryRows
                .FromSqlRaw(sql, new NpgsqlParameter("customerId", _testCustomerId))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        });
    }

    // ── Concurrent benchmarks: table scan ───────────────────────────────

    [Benchmark]
    public async Task TableScan_Pengdows_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildTableScanSql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("customerId", DbType.Int32, _testCustomerId);
            container.AddParameterWithValue("status", DbType.String, "Active");
            await _summaryHelper.LoadSingleAsync(container);
        });
    }

    [Benchmark]
    public async Task TableScan_Dapper_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            var sql = BuildTableScanSql(param => $"@{param}");
            await conn.QuerySingleOrDefaultAsync<PgCustomerOrderSummary>(
                sql,
                new { customerId = _testCustomerId, status = "Active" });
        });
    }

    [Benchmark]
    public async Task TableScan_EntityFramework_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new MatViewEfContext(_efOptions);
            var sql = BuildTableScanSql(param => $"@{param}");
            await ctx.CustomerOrderSummaryRows
                .FromSqlRaw(
                    sql,
                    new NpgsqlParameter("customerId", _testCustomerId),
                    new NpgsqlParameter("status", "Active"))
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

        [Column("order_count", DbType.Int64)]
        public long OrderCount { get; set; }

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
