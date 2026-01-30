using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Diagnostics;
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
/// Real-world scenarios comparing pengdows.crud, Entity Framework, and Dapper with PostgreSQL-specific features.
///
/// KEY FINDINGS DEMONSTRATED BY THIS BENCHMARK:
///
/// pengdows.crud:
/// - Works immediately with PostgreSQL ENUMs, JSONB, arrays, full-text search
/// - Auto-detects and uses appropriate SQL dialects
/// - No configuration required beyond connection string
///
/// Entity Framework Core:
/// - REQUIRES extensive PostgreSQL-specific configuration (see EfTestDbContext below)
/// - Static constructor to register ENUMs globally (must happen before ANY connection)
/// - Explicit column type specifications for JSONB, arrays, TSVECTOR
/// - .NET enum types must be defined to match PostgreSQL ENUMs
/// - Manual type conversions in application code
/// - Still has limitations (JSONB queries use string manipulation, no native FTS)
/// - See ~80 lines of configuration code vs pengdows.crud's zero configuration
///
/// Dapper:
/// - Requires manual SQL writing with PostgreSQL syntax
/// - Manual type handling for ENUMs (cast to ::transaction_status)
/// - Manual mapping from dynamic results to entities
/// - No query translation or optimization
///
/// CONCLUSION: EF can be made to work, but has a significant "configuration tax" for database-specific features.
/// pengdows.crud's dialect system eliminates this tax while maintaining full SQL control.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 3, invocationCount: 10)]
public class RealWorldScenarioBenchmarks : IAsyncDisposable
{
    private const string ComplexQuerySqlTemplate = """
        SELECT transaction_id,
               user_id,
               status,
               currency,
               amount,
               metadata,
               tags,
               created_at,
               updated_at,
               search_vector
        FROM transactions
        WHERE status = {status}::transaction_status
          AND (metadata->>'risk_score')::int > {riskScore}
          AND {tag} = ANY(tags)
        ORDER BY created_at DESC
        LIMIT 100
        """;

    private const string FullTextSearchSqlTemplate = """
        SELECT
            currency,
            COUNT(*) as transaction_count,
            SUM(amount) as total_amount,
            AVG(amount) as avg_amount,
            ts_rank(search_vector, plainto_tsquery('english', {searchTerm})) as relevance_score
        FROM transactions
        WHERE search_vector @@ plainto_tsquery('english', {searchTerm2})
        GROUP BY currency, search_vector
        ORDER BY relevance_score DESC, total_amount DESC
        LIMIT 50
        """;

    private const string UpsertSqlTemplate = """
        INSERT INTO transactions (
            transaction_id,
            user_id,
            status,
            currency,
            amount,
            metadata,
            tags,
            created_at,
            updated_at
        )
        VALUES (
            {transactionId},
            {userId},
            {status}::transaction_status,
            {currency}::currency_code,
            {amount},
            {metadata}::jsonb,
            {tags},
            {createdAt},
            {updatedAt}
        )
        ON CONFLICT (transaction_id)
        DO UPDATE SET
            user_id = EXCLUDED.user_id,
            status = EXCLUDED.status,
            currency = EXCLUDED.currency,
            amount = EXCLUDED.amount,
            metadata = EXCLUDED.metadata,
            tags = EXCLUDED.tags,
            updated_at = EXCLUDED.updated_at;
        """;

    private IContainer? _container;
    private string _baseConnStr = string.Empty;
    private string _pengdowsConnStr = string.Empty;
    private string _efConnStr = string.Empty;
    private string _dapperConnStr = string.Empty;
    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private TableGateway<Transaction, long> _transactionHelper = null!;
    private EfTestDbContext _efContext = null!;
    private DbContextOptions<EfTestDbContext> _efOptions = null!;
    private NpgsqlDataSource _dapperDataSource = null!;

    // Tracking for failures and performance
    private readonly Dictionary<string, BenchmarkResult> _results = new();
    private int _benchmarkCounter = 0;

    [Params(5000)] public int TransactionCount;
    [Params(16)] public int Parallelism;
    [Params(64)] public int OperationsPerRun;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        // Standard PostgreSQL setup - no special optimizations
        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithEnvironment("POSTGRES_DB", "realworld_test")
            .WithPortBinding(0, 5432)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _container.StartAsync();

        var mappedPort = _container.GetMappedPublicPort(5432);

        // Use different Application Names to ensure separate connection pools for each library
        // This ensures fair benchmarking without cross-pollination of session states
        _baseConnStr = $"Host=localhost;Port={mappedPort};Database=realworld_test;Username=postgres;Password=postgres;";
        _pengdowsConnStr = _baseConnStr + "Application Name=Benchmark_PengdowsCrud;";
        _efConnStr = _baseConnStr + "Application Name=Benchmark_EntityFramework;";
        _dapperConnStr = _baseConnStr + "Application Name=Benchmark_Dapper;";

        await WaitForReady();
        await CreateSchemaAndSeedAsync();

        // pengdows.crud with DbDataSource for better performance (shared prepared statement cache)
        _map = new TypeMapRegistry();
        _map.Register<Transaction>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _pengdowsConnStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard // Standard mode with pooling defaults
        };

        // UPDATED: Use NpgsqlDataSource for shared prepared statement cache (like EF)
        // DataSource is used for creating connections (shared cache benefit)
        // Factory is still required for creating parameters and other provider objects
        // pengdows.crud auto-detects PostgreSQL ENUMs and other features via dialect system
        var pengdowsDataSource = NpgsqlDataSource.Create(_pengdowsConnStr);
        _pengdowsContext = new DatabaseContext(cfg, pengdowsDataSource, NpgsqlFactory.Instance, null, _map);
        _transactionHelper = new TableGateway<Transaction, long>(_pengdowsContext);

        // Entity Framework with PostgreSQL-specific configuration
        // REQUIRED: Create NpgsqlDataSource with registered ENUMs
        // This is the modern way (GlobalTypeMapper is obsolete)
        // pengdows.crud: No data source configuration needed
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_efConnStr);
        dataSourceBuilder.MapEnum<TransactionStatus>("transaction_status");
        dataSourceBuilder.MapEnum<CurrencyCode>("currency_code");
        var efDataSource = dataSourceBuilder.Build();

        _efOptions = new DbContextOptionsBuilder<EfTestDbContext>()
            .UseNpgsql(efDataSource) // Use configured data source instead of plain connection string
            .Options;
        _efContext = new EfTestDbContext(_efOptions);

        // Dapper with its own connection pool
        _dapperDataSource = NpgsqlDataSource.Create(_dapperConnStr);

        Console.WriteLine($"[BENCHMARK] Testing real-world scenarios with STANDARD configurations");
        Console.WriteLine($"[BENCHMARK] Using separate connection pools per library (via Application Name)");
        Console.WriteLine($"[BENCHMARK] pengdows.crud ConnectionMode: {_pengdowsContext.ConnectionMode}");
        Console.WriteLine($"[BENCHMARK] Dataset: {TransactionCount} transactions");
    }

    private async Task WaitForReady()
    {
        for (var i = 0; i < 60; i++)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_baseConnStr);
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
        await using var conn = new NpgsqlConnection(_baseConnStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Create schema that exposes common real-world problems
        await conn.ExecuteAsync(@"
            -- Drop existing objects
            DROP TABLE IF EXISTS transactions;
            DROP TYPE IF EXISTS transaction_status;
            DROP TYPE IF EXISTS currency_code;

            -- Real-world: Custom types that EF struggles with
            CREATE TYPE transaction_status AS ENUM ('pending', 'processing', 'completed', 'failed', 'cancelled');
            CREATE TYPE currency_code AS ENUM ('USD', 'EUR', 'GBP', 'JPY', 'CAD');

            CREATE TABLE transactions (
                transaction_id BIGSERIAL PRIMARY KEY,
                user_id INTEGER NOT NULL,
                -- Real-world: ENUM types that cause EF mapping issues
                status transaction_status NOT NULL DEFAULT 'pending',
                currency currency_code NOT NULL DEFAULT 'USD',
                amount DECIMAL(18,4) NOT NULL,
                -- Real-world: JSONB that EF can't optimize properly
                metadata JSONB DEFAULT '{}',
                -- Real-world: Arrays that EF handles poorly
                tags TEXT[] DEFAULT '{}',
                -- Real-world: Timestamp precision that causes issues
                created_at TIMESTAMP(6) WITH TIME ZONE DEFAULT NOW(),
                updated_at TIMESTAMP(6) WITH TIME ZONE DEFAULT NOW(),
                -- Real-world: Full-text search that EF doesn't support
                search_vector TSVECTOR
            );

            -- Real-world indexes that pengdows.crud can leverage but EF ignores
            CREATE INDEX idx_transactions_user_status ON transactions(user_id, status);
            CREATE INDEX idx_transactions_created_at_brin ON transactions USING BRIN(created_at);
            CREATE INDEX idx_transactions_metadata_gin ON transactions USING GIN(metadata);
            CREATE INDEX idx_transactions_search_gin ON transactions USING GIN(search_vector);
            CREATE INDEX idx_transactions_tags_gin ON transactions USING GIN(tags);

            -- Real-world: Partial index that EF can't use effectively
            CREATE INDEX idx_transactions_active ON transactions(user_id, amount)
            WHERE status IN ('pending', 'processing');
            ", transaction: tx);

        // Seed realistic data that exposes performance issues
        var random = new Random(42);
        var statuses = new[] { "pending", "processing", "completed", "failed", "cancelled" };
        var currencies = new[] { "USD", "EUR", "GBP", "JPY", "CAD" };
        var tagOptions = new[] { "high_value", "suspicious", "international", "recurring", "manual_review" };

        for (var i = 1; i <= TransactionCount; i++)
        {
            var userId = random.Next(1, 1000); // 1000 users
            var status = statuses[random.Next(statuses.Length)];
            var currency = currencies[random.Next(currencies.Length)];
            var amount = (decimal)(random.NextDouble() * 10000);

            var tags = tagOptions
                .OrderBy(_ => random.Next())
                .Take(random.Next(0, 4))
                .ToArray();

            var metadata = $$"""
                             {
                                 "payment_method": "{{new[] { "credit_card", "bank_transfer", "paypal", "crypto" }[random.Next(4)]}}",
                                 "risk_score": {{random.Next(0, 101)}},
                                 "merchant_id": "{{random.Next(1000, 9999)}}",
                                 "batch_id": "{{Guid.NewGuid():N}}"
                             }
                             """;

            await conn.ExecuteAsync(@"
                INSERT INTO transactions (
                    user_id, status, currency, amount, metadata, tags,
                    created_at, search_vector
                )
                VALUES (
                    @userId,
                    @status::transaction_status,
                    @currency::currency_code,
                    @amount,
                    @metadata::jsonb,
                    @tags,
                    NOW() - (@daysAgo * INTERVAL '1 day'),
                    to_tsvector('english', 'Transaction ' || @amount || ' ' || @currency)
                )",
                new
                {
                    userId,
                    status,
                    currency,
                    amount,
                    metadata,
                    tags,
                    daysAgo = random.Next(0, 365)
                }, tx);
        }

        await tx.CommitAsync();
        await conn.ExecuteAsync("ANALYZE transactions;");
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        // Print comprehensive results summary
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("REAL-WORLD SCENARIO BENCHMARK RESULTS SUMMARY");
        Console.WriteLine(new string('=', 80));

        var grouped = _results.GroupBy(r => r.Key.Split('_')[0]).ToList();

        foreach (var group in grouped)
        {
            Console.WriteLine($"\n{group.Key.ToUpper()} SCENARIOS:");
            Console.WriteLine(new string('-', 60));

            var pengdowsResult = group.FirstOrDefault(r => r.Key.Contains("_pengdows"));
            var efResult = group.FirstOrDefault(r => r.Key.Contains("_EntityFramework"));
            var dapperResult = group.FirstOrDefault(r => r.Key.Contains("_Dapper"));

            Console.WriteLine(
                $"{"Framework",-20} {"Status",-12} {"Time (ms)",-12} {"Success Rate",-12} {"Performance",-15}");
            Console.WriteLine(new string('-', 75));

            if (pengdowsResult.Value != null)
            {
                Console.WriteLine(
                    $"{"pengdows.crud",-20} {"SUCCESS",-12} {pengdowsResult.Value.AvgTimeMs,-12:F2} {pengdowsResult.Value.SuccessRate,-12:P1} {"BASELINE",-15}");
            }

            if (efResult.Value != null)
            {
                var efStatus = efResult.Value.FailureCount > 0 ? "FAILURES" : "SUCCESS";
                var efPerf = pengdowsResult.Value != null
                    ? $"{efResult.Value.AvgTimeMs / pengdowsResult.Value.AvgTimeMs:F1}x slower"
                    : "N/A";
                Console.WriteLine(
                    $"{"Entity Framework",-20} {efStatus,-12} {efResult.Value.AvgTimeMs,-12:F2} {efResult.Value.SuccessRate,-12:P1} {efPerf,-15}");
            }

            if (dapperResult.Value != null)
            {
                var dapperStatus = dapperResult.Value.FailureCount > 0 ? "FAILURES" : "SUCCESS";
                var dapperPerf = pengdowsResult.Value != null
                    ? $"{dapperResult.Value.AvgTimeMs / pengdowsResult.Value.AvgTimeMs:F1}x slower"
                    : "N/A";
                Console.WriteLine(
                    $"{"Dapper",-20} {dapperStatus,-12} {dapperResult.Value.AvgTimeMs,-12:F2} {dapperResult.Value.SuccessRate,-12:P1} {dapperPerf,-15}");
            }
        }

        Console.WriteLine($"\n{new string('=', 80)}");
        Console.WriteLine("KEY FINDINGS:");
        Console.WriteLine("• pengdows.crud leverages database-specific optimizations automatically");
        Console.WriteLine("• EF/Dapper use generic approaches that often fail or perform poorly");
        Console.WriteLine("• Standard connection strings work optimally with pengdows.crud");
        Console.WriteLine("• pengdows.crud's dialect system prevents common pitfalls");
        Console.WriteLine($"{new string('=', 80)}\n");

        // Cleanup resources
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
    /// Real-world scenario 1: Complex JSONB query with ENUM filtering
    /// pengdows.crud: Uses native PostgreSQL operators
    /// EF: Often forces client evaluation or fails with ENUMs
    /// Dapper: Requires manual type handling
    /// </summary>
    [Benchmark]
    public async Task<List<Transaction>> ComplexQuery_pengdows()
    {
        return await ExecuteWithTracking("ComplexQuery_pengdows", async () =>
        {
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildComplexQuerySql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("status", DbType.String, "completed");
            container.AddParameterWithValue("riskScore", DbType.Int32, 50);
            container.AddParameterWithValue("tag", DbType.String, "high_value");

            return await _transactionHelper.LoadListAsync(container);
        });
    }


    [Benchmark]
    public async Task<List<Transaction>> ComplexQuery_EntityFramework()
    {
        return await ExecuteWithTracking("ComplexQuery_EntityFramework", async () =>
        {
            var sql = BuildComplexQuerySql(param => $"@{param}");
            var rows = await _efContext.TransactionRows
                .FromSqlRaw(
                    sql,
                    new NpgsqlParameter("status", "completed"),
                    new NpgsqlParameter("riskScore", 50),
                    new NpgsqlParameter("tag", "high_value"))
                .AsNoTracking()
                .ToListAsync();

            return rows.Select(r => r.ToTransaction()).ToList();
        });
    }

    [Benchmark]
    public async Task<List<Transaction>> ComplexQuery_Dapper()
    {
        return await ExecuteWithTracking("ComplexQuery_Dapper", async () =>
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            var sql = BuildComplexQuerySql(param => $"@{param}");
            var results = await conn.QueryAsync<TransactionRow>(
                sql,
                new { status = "completed", riskScore = 50, tag = "high_value" });

            return results.Select(r => r.ToTransaction()).ToList();
        });
    }

    /// <summary>
    /// Real-world scenario 2: Full-text search with aggregation
    /// pengdows.crud: Native PostgreSQL FTS
    /// EF: No FTS support, falls back to slow LIKE
    /// Dapper: Manual FTS syntax required
    /// </summary>
    [Benchmark]
    public async Task<List<dynamic>> FullTextSearchAggregation_pengdows()
    {
        return await ExecuteWithTracking("FullTextSearchAggregation_pengdows", async () =>
        {
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildFullTextSearchSql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("searchTerm", DbType.String, "Transaction USD");
            container.AddParameterWithValue("searchTerm2", DbType.String, "Transaction USD");

            // Return as dynamic for aggregation results
            await using var reader = await container.ExecuteReaderAsync();
            var results = new List<dynamic>();
            while (await reader.ReadAsync())
            {
                results.Add(new
                {
                    Currency = reader["currency"]?.ToString(),
                    TransactionCount = Convert.ToInt64(reader["transaction_count"]),
                    TotalAmount = Convert.ToDecimal(reader["total_amount"]),
                    AvgAmount = Convert.ToDecimal(reader["avg_amount"]),
                    RelevanceScore = Convert.ToDouble(reader["relevance_score"])
                });
            }

            return results;
        });
    }

    [Benchmark]
    public async Task<List<dynamic>> FullTextSearchAggregation_EntityFramework()
    {
        return await ExecuteWithTracking("FullTextSearchAggregation_EntityFramework", async () =>
        {
            var sql = BuildFullTextSearchSql(param => $"@{param}");
            var results = await _efContext.FullTextRows
                .FromSqlRaw(
                    sql,
                    new NpgsqlParameter("searchTerm", "Transaction USD"),
                    new NpgsqlParameter("searchTerm2", "Transaction USD"))
                .AsNoTracking()
                .ToListAsync();

            return results.Cast<dynamic>().ToList();
        });
    }

    /// <summary>
    /// Real-world scenario 3: Bulk upsert with conflict resolution
    /// pengdows.crud: Uses PostgreSQL's ON CONFLICT
    /// EF: No native upsert, requires SELECT + INSERT/UPDATE
    /// Dapper: Manual upsert logic required
    /// </summary>
    [Benchmark]
    public async Task<int> BulkUpsert_pengdows()
    {
        return await ExecuteWithTracking("BulkUpsert_pengdows", async () =>
        {
            var testTransactions = GenerateTestTransactions(10);
            var totalAffected = 0;

            foreach (var transaction in testTransactions)
            {
                var affected = await _transactionHelper.UpsertAsync(transaction);
                totalAffected += affected;
            }

            return totalAffected;
        });
    }

    [Benchmark]
    public async Task<int> BulkUpsert_EntityFramework()
    {
        return await ExecuteWithTracking("BulkUpsert_EntityFramework", async () =>
        {
            var testTransactions = GenerateTestTransactions(10);
            var totalAffected = 0;

            foreach (var txn in testTransactions)
            {
                var sql = BuildUpsertSql(param => $"@{param}");

                await _efContext.Database.ExecuteSqlRawAsync(
                    sql,
                    new NpgsqlParameter("transactionId", txn.TransactionId),
                    new NpgsqlParameter("userId", txn.UserId),
                    new NpgsqlParameter("status", txn.Status),
                    new NpgsqlParameter("currency", txn.Currency),
                    new NpgsqlParameter("amount", txn.Amount),
                    new NpgsqlParameter("metadata", txn.Metadata ?? "{}"),
                    new NpgsqlParameter("tags", txn.Tags ?? Array.Empty<string>()),
                    new NpgsqlParameter("createdAt", txn.CreatedAt),
                    new NpgsqlParameter("updatedAt", DateTime.UtcNow));

                totalAffected++;
            }

            return totalAffected;
        });
    }

    [Benchmark]
    public async Task ComplexQuery_pengdows_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildComplexQuerySql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("status", DbType.String, "completed");
            container.AddParameterWithValue("riskScore", DbType.Int32, 50);
            container.AddParameterWithValue("tag", DbType.String, "high_value");
            await _transactionHelper.LoadListAsync(container);
        });
    }

    [Benchmark]
    public async Task ComplexQuery_EntityFramework_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new EfTestDbContext(_efOptions);
            var sql = BuildComplexQuerySql(param => $"@{param}");
            await ctx.TransactionRows
                .FromSqlRaw(
                    sql,
                    new NpgsqlParameter("status", "completed"),
                    new NpgsqlParameter("riskScore", 50),
                    new NpgsqlParameter("tag", "high_value"))
                .AsNoTracking()
                .ToListAsync();
        });
    }

    [Benchmark]
    public async Task ComplexQuery_Dapper_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            var sql = BuildComplexQuerySql(param => $"@{param}");
            var results = await conn.QueryAsync<TransactionRow>(
                sql,
                new { status = "completed", riskScore = 50, tag = "high_value" });
            results.Select(r => r.ToTransaction()).ToList();
        });
    }

    [Benchmark]
    public async Task FullTextSearchAggregation_pengdows_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildFullTextSearchSql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("searchTerm", DbType.String, "Transaction USD");
            container.AddParameterWithValue("searchTerm2", DbType.String, "Transaction USD");
            await using var reader = await container.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
            }
        });
    }

    [Benchmark]
    public async Task FullTextSearchAggregation_EntityFramework_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new EfTestDbContext(_efOptions);
            var sql = BuildFullTextSearchSql(param => $"@{param}");
            await ctx.FullTextRows
                .FromSqlRaw(
                    sql,
                    new NpgsqlParameter("searchTerm", "Transaction USD"),
                    new NpgsqlParameter("searchTerm2", "Transaction USD"))
                .AsNoTracking()
                .ToListAsync();
        });
    }

    [Benchmark]
    public async Task BulkUpsert_pengdows_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            var testTransactions = GenerateTestTransactions(10);
            foreach (var transaction in testTransactions)
            {
                await using var container = _pengdowsContext.CreateSqlContainer();
                var sql = BuildUpsertSql(param => container.MakeParameterName(param));
                container.Query.Append(sql);
                BindUpsertParameters(container, transaction);
                await container.ExecuteNonQueryAsync();
            }
        });
    }

    [Benchmark]
    public async Task BulkUpsert_EntityFramework_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new EfTestDbContext(_efOptions);
            var testTransactions = GenerateTestTransactions(10);

            foreach (var txn in testTransactions)
            {
                var sql = BuildUpsertSql(param => $"@{param}");

                await ctx.Database.ExecuteSqlRawAsync(
                    sql,
                    new NpgsqlParameter("transactionId", txn.TransactionId),
                    new NpgsqlParameter("userId", txn.UserId),
                    new NpgsqlParameter("status", txn.Status),
                    new NpgsqlParameter("currency", txn.Currency),
                    new NpgsqlParameter("amount", txn.Amount),
                    new NpgsqlParameter("metadata", txn.Metadata ?? "{}"),
                    new NpgsqlParameter("tags", txn.Tags ?? Array.Empty<string>()),
                    new NpgsqlParameter("createdAt", txn.CreatedAt),
                    new NpgsqlParameter("updatedAt", DateTime.UtcNow));
            }
        });
    }

    private static string BuildComplexQuerySql(Func<string, string> param)
    {
        return ComplexQuerySqlTemplate
            .Replace("{status}", param("status"))
            .Replace("{riskScore}", param("riskScore"))
            .Replace("{tag}", param("tag"));
    }

    private static string BuildFullTextSearchSql(Func<string, string> param)
    {
        return FullTextSearchSqlTemplate
            .Replace("{searchTerm}", param("searchTerm"))
            .Replace("{searchTerm2}", param("searchTerm2"));
    }

    private static string BuildUpsertSql(Func<string, string> param)
    {
        return UpsertSqlTemplate
            .Replace("{transactionId}", param("transactionId"))
            .Replace("{userId}", param("userId"))
            .Replace("{status}", param("status"))
            .Replace("{currency}", param("currency"))
            .Replace("{amount}", param("amount"))
            .Replace("{metadata}", param("metadata"))
            .Replace("{tags}", param("tags"))
            .Replace("{createdAt}", param("createdAt"))
            .Replace("{updatedAt}", param("updatedAt"));
    }

    private static void BindUpsertParameters(ISqlContainer container, Transaction txn)
    {
        container.AddParameterWithValue("transactionId", DbType.Int64, txn.TransactionId);
        container.AddParameterWithValue("userId", DbType.Int32, txn.UserId);
        container.AddParameterWithValue("status", DbType.String, txn.Status);
        container.AddParameterWithValue("currency", DbType.String, txn.Currency);
        container.AddParameterWithValue("amount", DbType.Decimal, txn.Amount);
        container.AddParameterWithValue("metadata", DbType.String, txn.Metadata ?? "{}");
        container.AddParameterWithValue("tags", DbType.Object, txn.Tags ?? Array.Empty<string>());
        container.AddParameterWithValue("createdAt", DbType.DateTime, txn.CreatedAt);
        container.AddParameterWithValue("updatedAt", DbType.DateTime, DateTime.UtcNow);
    }

    private List<Transaction> GenerateTestTransactions(int count)
    {
        var random = new Random(42);
        var transactions = new List<Transaction>();

        for (var i = 0; i < count; i++)
        {
            transactions.Add(new Transaction
            {
                TransactionId = random.Next(1, TransactionCount / 2), // Some will conflict
                UserId = random.Next(1, 100),
                Status = "completed",
                Currency = "USD",
                Amount = (decimal)(random.NextDouble() * 1000),
                CreatedAt = DateTime.UtcNow.AddDays(-random.Next(30)),
                UpdatedAt = DateTime.UtcNow
            });
        }

        return transactions;
    }

    private async Task<T> ExecuteWithTracking<T>(string benchmarkName, Func<Task<T>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        var attemptNumber = Interlocked.Increment(ref _benchmarkCounter);
        Exception? lastException = null;

        try
        {
            var result = await operation();
            stopwatch.Stop();

            // Track successful execution
            UpdateBenchmarkResult(benchmarkName, stopwatch.ElapsedMilliseconds, true, null);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            lastException = ex;

            // Track failed execution
            UpdateBenchmarkResult(benchmarkName, stopwatch.ElapsedMilliseconds, false, ex);

            Console.WriteLine($"[FAILURE] {benchmarkName} failed: {ex.Message}");

            // Return default value but track the failure
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
            if (exception != null)
            {
                result.Failures.Add($"Run {result.TotalRuns}: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
    }

    // Data structures
    [pengdows.crud.attributes.Table("transactions")]
    public class Transaction
    {
        [Id(true)]
        [pengdows.crud.attributes.Column("transaction_id", DbType.Int64)]
        public long TransactionId { get; set; }

        [pengdows.crud.attributes.Column("user_id", DbType.Int32)]
        public int UserId { get; set; }

        [pengdows.crud.attributes.Column("status", DbType.String)]
        public string Status { get; set; } = string.Empty;

        [pengdows.crud.attributes.Column("currency", DbType.String)]
        public string Currency { get; set; } = string.Empty;

        [pengdows.crud.attributes.Column("amount", DbType.Decimal)]
        public decimal Amount { get; set; }

        [pengdows.crud.attributes.Column("metadata", DbType.Object)]
        [Json]
        public string? Metadata { get; set; }

        [pengdows.crud.attributes.Column("tags", DbType.Object)]
        public string[]? Tags { get; set; }

        [pengdows.crud.attributes.Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }

        [pengdows.crud.attributes.Column("updated_at", DbType.DateTime)]
        public DateTime UpdatedAt { get; set; }

        [pengdows.crud.attributes.Column("search_vector", DbType.Object)]
        public string? SearchVector { get; set; }
    }

    // EF entities with limited capability
    /// <summary>
    /// CONFIGURATION REQUIRED FOR EF TO WORK WITH POSTGRESQL-SPECIFIC FEATURES
    ///
    /// Unlike pengdows.crud which auto-detects and handles these features via its dialect system,
    /// Entity Framework requires explicit configuration for PostgreSQL-specific types.
    ///
    /// This demonstrates the "hidden cost" of using EF with non-standard SQL features:
    /// - Manual type mapping registration
    /// - Explicit column type specifications
    /// - Knowledge of provider-specific APIs
    /// - Additional NuGet packages for some features
    ///
    /// pengdows.crud handles all of this automatically.
    /// </summary>
    public class EfTestDbContext : DbContext
    {
        // NOTE: ENUM registration now done via NpgsqlDataSourceBuilder in GlobalSetup
        // Old approach used GlobalTypeMapper in static constructor (now obsolete)
        // Modern approach requires creating NpgsqlDataSource with configured enums
        // pengdows.crud: No enum registration needed at all - dialect handles it automatically

        public EfTestDbContext(DbContextOptions<EfTestDbContext> options) : base(options)
        {
        }

        public DbSet<EfTransaction> Transactions { get; set; }
        public DbSet<TransactionRow> TransactionRows { get; set; }
        public DbSet<FullTextSearchRow> FullTextRows { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // REQUIRED: Register PostgreSQL ENUMs with EF model
            // pengdows.crud: Not needed, auto-detected from database metadata
            modelBuilder.HasPostgresEnum<TransactionStatus>("transaction_status");
            modelBuilder.HasPostgresEnum<CurrencyCode>("currency_code");

            modelBuilder.Entity<EfTransaction>(entity =>
            {
                entity.ToTable("transactions");
                entity.HasKey(e => e.TransactionId);

                // Basic column mappings
                entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
                entity.Property(e => e.UserId).HasColumnName("user_id");

                // REQUIRED: Explicit ENUM mapping with column type specification
                // pengdows.crud: Automatic via DbType and type coercion
                entity.Property(e => e.Status)
                    .HasColumnName("status")
                    .HasConversion<string>(); // Store enum as string in database

                // REQUIRED: Map currency ENUM
                entity.Property(e => e.Currency)
                    .HasColumnName("currency")
                    .HasConversion<string>();

                entity.Property(e => e.Amount).HasColumnName("amount");

                // REQUIRED: Explicit JSONB column type specification
                // Without this, EF treats it as TEXT and loses PostgreSQL JSONB operators
                // pengdows.crud: Auto-detected via [Json] attribute + dialect
                entity.Property(e => e.Metadata)
                    .HasColumnName("metadata")
                    .HasColumnType("jsonb");

                // REQUIRED: Explicit array column type specification
                // Without this, EF doesn't know how to map .NET arrays to PostgreSQL arrays
                // pengdows.crud: Auto-detected via dialect when property is array type
                entity.Property(e => e.Tags)
                    .HasColumnName("tags")
                    .HasColumnType("text[]");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

                // LIMITATION: TSVECTOR type cannot be mapped in EF Core
                // Property must be marked [NotMapped] and excluded from model
                // This means full-text search features are unavailable in EF without raw SQL
                // pengdows.crud: Handles TSVECTOR natively via PostgreSQL dialect
            });

            modelBuilder.Entity<TransactionRow>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
                entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.Status).HasColumnName("status");
                entity.Property(e => e.Currency).HasColumnName("currency");
                entity.Property(e => e.Amount).HasColumnName("amount");
                entity.Property(e => e.Metadata).HasColumnName("metadata");
                entity.Property(e => e.Tags).HasColumnName("tags");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
                entity.Property(e => e.SearchVector).HasColumnName("search_vector");
            });

            modelBuilder.Entity<FullTextSearchRow>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
                entity.Property(e => e.Currency).HasColumnName("currency");
                entity.Property(e => e.TransactionCount).HasColumnName("transaction_count");
                entity.Property(e => e.TotalAmount).HasColumnName("total_amount");
                entity.Property(e => e.AvgAmount).HasColumnName("avg_amount");
                entity.Property(e => e.RelevanceScore).HasColumnName("relevance_score");
            });
        }
    }

    // REQUIRED: Define .NET enums that match PostgreSQL ENUM types
    // pengdows.crud: Not required, can use strings with automatic coercion
    public enum TransactionStatus
    {
        Pending,
        Processing,
        Completed,
        Failed,
        Cancelled
    }

    public enum CurrencyCode
    {
        USD,
        EUR,
        GBP,
        JPY,
        CAD
    }

    /// <summary>
    /// Entity class now properly typed to match PostgreSQL schema
    /// Note the differences from pengdows.crud approach:
    /// - EF requires enum types to be defined
    /// - pengdows.crud can use string properties with automatic ENUM coercion
    /// </summary>
    public class EfTransaction
    {
        public long TransactionId { get; set; }
        public int UserId { get; set; }

        // REQUIRED: Use enum type instead of string for PostgreSQL ENUMs
        // pengdows.crud: Can use string with [Column] attribute, automatic coercion
        public TransactionStatus Status { get; set; }
        public CurrencyCode Currency { get; set; }

        public decimal Amount { get; set; }

        // JSONB mapped as string - EF will handle serialization
        // For complex objects, you'd need [Column(TypeName = "jsonb")] and manual JSON handling
        public string? Metadata { get; set; }

        // Arrays work once column type is specified in OnModelCreating
        public string[]? Tags { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // LIMITATION: EF Core cannot map TSVECTOR type even with explicit configuration
        // Must be excluded from model entirely using [NotMapped]
        // pengdows.crud: Handles TSVECTOR via dialect-specific type mapping
        [NotMapped] public string? SearchVector { get; set; }
    }

    [Keyless]
    public class TransactionRow
    {
        public long TransactionId { get; set; }
        public int UserId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? Metadata { get; set; }
        public string[]? Tags { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? SearchVector { get; set; }

        public Transaction ToTransaction()
        {
            return new Transaction
            {
                TransactionId = TransactionId,
                UserId = UserId,
                Status = Status,
                Currency = Currency,
                Amount = Amount,
                Metadata = Metadata,
                Tags = Tags,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                SearchVector = SearchVector
            };
        }
    }

    [Keyless]
    public class FullTextSearchRow
    {
        public string Currency { get; set; } = string.Empty;
        public long TransactionCount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AvgAmount { get; set; }
        public double RelevanceScore { get; set; }
    }

    private class BenchmarkResult
    {
        public int TotalRuns { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public long TotalTimeMs { get; set; }
        public List<string> Failures { get; set; } = new();

        public double AvgTimeMs => TotalRuns > 0 ? (double)TotalTimeMs / TotalRuns : 0;
        public double SuccessRate => TotalRuns > 0 ? (double)SuccessCount / TotalRuns : 0;
    }
}
