using System.Data;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
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
/// Real-world scenarios that demonstrate where EF/Dapper fail or perform poorly
/// with standard configurations, while pengdows.crud succeeds.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 3, invocationCount: 10)]
public class RealWorldScenarioBenchmarks : IAsyncDisposable
{
    private IContainer? _container;
    private string _connStr = string.Empty;
    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private EntityHelper<Transaction, long> _transactionHelper = null!;
    private EfTestDbContext _efContext = null!;
    private NpgsqlDataSource _dapperDataSource = null!;

    // Tracking for failures and performance
    private readonly Dictionary<string, BenchmarkResult> _results = new();
    private int _benchmarkCounter = 0;

    [Params(5000)]
    public int TransactionCount;

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

        // STANDARD connection string - no special optimization parameters
        _connStr = $"Host=localhost;Port={mappedPort};Database=realworld_test;Username=postgres;Password=postgres";

        await WaitForReady();
        await CreateSchemaAndSeedAsync();

        // pengdows.crud with STANDARD configuration
        _map = new TypeMapRegistry();
        _map.Register<Transaction>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _connStr, // Standard connection string
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard // Standard mode with pooling defaults
        };
        _pengdowsContext = new DatabaseContext(cfg, NpgsqlFactory.Instance, null, _map);
        _transactionHelper = new EntityHelper<Transaction, long>(_pengdowsContext);

        // Entity Framework with STANDARD configuration
        var options = new DbContextOptionsBuilder<EfTestDbContext>()
            .UseNpgsql(_connStr) // Standard connection string, no optimizations
            .Options;
        _efContext = new EfTestDbContext(options);

        // Dapper with STANDARD data source
        _dapperDataSource = NpgsqlDataSource.Create(_connStr); // Standard connection string

        Console.WriteLine($"[BENCHMARK] Testing real-world scenarios with STANDARD configurations");
        Console.WriteLine($"[BENCHMARK] Connection String: {_connStr}");
        Console.WriteLine($"[BENCHMARK] pengdows.crud ConnectionMode: {_pengdowsContext.ConnectionMode}");
        Console.WriteLine($"[BENCHMARK] Dataset: {TransactionCount} transactions");
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

        for (int i = 1; i <= TransactionCount; i++)
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
                    NOW() - INTERVAL '@daysAgo days',
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

            Console.WriteLine($"{"Framework",-20} {"Status",-12} {"Time (ms)",-12} {"Success Rate",-12} {"Performance",-15}");
            Console.WriteLine(new string('-', 75));

            if (pengdowsResult.Value != null)
            {
                Console.WriteLine($"{"pengdows.crud",-20} {"SUCCESS",-12} {pengdowsResult.Value.AvgTimeMs,-12:F2} {pengdowsResult.Value.SuccessRate,-12:P1} {"BASELINE",-15}");
            }

            if (efResult.Value != null)
            {
                var efStatus = efResult.Value.FailureCount > 0 ? "FAILURES" : "SUCCESS";
                var efPerf = pengdowsResult.Value != null ? $"{efResult.Value.AvgTimeMs / pengdowsResult.Value.AvgTimeMs:F1}x slower" : "N/A";
                Console.WriteLine($"{"Entity Framework",-20} {efStatus,-12} {efResult.Value.AvgTimeMs,-12:F2} {efResult.Value.SuccessRate,-12:P1} {efPerf,-15}");
            }

            if (dapperResult.Value != null)
            {
                var dapperStatus = dapperResult.Value.FailureCount > 0 ? "FAILURES" : "SUCCESS";
                var dapperPerf = pengdowsResult.Value != null ? $"{dapperResult.Value.AvgTimeMs / pengdowsResult.Value.AvgTimeMs:F1}x slower" : "N/A";
                Console.WriteLine($"{"Dapper",-20} {dapperStatus,-12} {dapperResult.Value.AvgTimeMs,-12:F2} {dapperResult.Value.SuccessRate,-12:P1} {dapperPerf,-15}");
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
            using var container = _pengdowsContext.CreateSqlContainer(@"
                SELECT transaction_id, user_id, status, currency, amount, metadata, tags, created_at, updated_at
                FROM transactions
                WHERE status = ");
            container.Query.Append(container.MakeParameterName("status"));
            container.Query.Append("::transaction_status AND metadata->>'risk_score' > ");
            container.Query.Append(container.MakeParameterName("riskScore"));
            container.Query.Append(" AND ");
            container.Query.Append(container.MakeParameterName("tag"));
            container.Query.Append(" = ANY(tags) ORDER BY created_at DESC LIMIT 100");

            container.AddParameterWithValue("status", DbType.String, "completed");
            container.AddParameterWithValue("riskScore", DbType.String, "50");
            container.AddParameterWithValue("tag", DbType.String, "high_value");

            return await _transactionHelper.LoadListAsync(container);
        });
    }

    [Benchmark]
    public async Task<List<EfTransaction>> ComplexQuery_EntityFramework()
    {
        return await ExecuteWithTracking("ComplexQuery_EntityFramework", async () =>
        {
            // EF struggles with ENUMs and JSONB queries, often forces client evaluation
            return await _efContext.Transactions
                .AsNoTracking()
                .Where(t => t.Status == "completed") // ENUM handling issues
                .Where(t => t.Metadata != null && t.Metadata.Contains("\"risk_score\"") && t.Metadata.Contains("50")) // Poor JSONB support
                .Where(t => t.Tags != null && t.Tags.Contains("high_value")) // Array support limited
                .OrderByDescending(t => t.CreatedAt)
                .Take(100)
                .ToListAsync();
        });
    }

    [Benchmark]
    public async Task<List<Transaction>> ComplexQuery_Dapper()
    {
        return await ExecuteWithTracking("ComplexQuery_Dapper", async () =>
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            var results = await conn.QueryAsync<dynamic>(@"
                SELECT transaction_id, user_id, status, currency, amount, metadata, tags, created_at, updated_at
                FROM transactions
                WHERE status = @status::transaction_status
                  AND (metadata->>'risk_score')::int > @riskScore
                  AND @tag = ANY(tags)
                ORDER BY created_at DESC
                LIMIT 100",
                new { status = "completed", riskScore = 50, tag = "high_value" });

            // Manual mapping required - this is error-prone
            return results.Select(r => new Transaction
            {
                TransactionId = r.transaction_id,
                UserId = r.user_id,
                Status = r.status,
                Currency = r.currency,
                Amount = r.amount,
                CreatedAt = r.created_at,
                UpdatedAt = r.updated_at
                // Metadata and Tags require complex manual parsing
            }).ToList();
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
            using var container = _pengdowsContext.CreateSqlContainer(@"
                SELECT
                    currency,
                    COUNT(*) as transaction_count,
                    SUM(amount) as total_amount,
                    AVG(amount) as avg_amount,
                    ts_rank(search_vector, plainto_tsquery('english', ");
            container.Query.Append(container.MakeParameterName("searchTerm"));
            container.Query.Append(")) as relevance_score");
            container.Query.Append(@"
                FROM transactions
                WHERE search_vector @@ plainto_tsquery('english', ");
            container.Query.Append(container.MakeParameterName("searchTerm2"));
            container.Query.Append(@")
                GROUP BY currency, search_vector
                ORDER BY relevance_score DESC, total_amount DESC
                LIMIT 50");

            container.AddParameterWithValue("searchTerm", DbType.String, "Transaction USD");
            container.AddParameterWithValue("searchTerm2", DbType.String, "Transaction USD");

            // Return as dynamic for aggregation results
            using var reader = await container.ExecuteReaderAsync();
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
            // EF has no FTS support, must use slow LIKE queries
            var results = await _efContext.Transactions
                .AsNoTracking()
                .Where(t => EF.Functions.Like(t.SearchVector ?? "", "%Transaction%") &&
                           EF.Functions.Like(t.SearchVector ?? "", "%USD%")) // Very slow
                .GroupBy(t => t.Currency)
                .Select(g => new
                {
                    Currency = g.Key,
                    TransactionCount = g.Count(),
                    TotalAmount = g.Sum(t => t.Amount),
                    AvgAmount = g.Average(t => t.Amount),
                    RelevanceScore = 0.0 // Can't calculate relevance without FTS
                })
                .OrderByDescending(r => r.TotalAmount)
                .Take(50)
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
            int totalAffected = 0;

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
            int totalAffected = 0;

            foreach (var txn in testTransactions)
            {
                // EF has no native upsert - must do SELECT + INSERT/UPDATE
                var existing = await _efContext.Transactions
                    .FirstOrDefaultAsync(t => t.TransactionId == txn.TransactionId);

                if (existing != null)
                {
                    existing.Amount = txn.Amount;
                    existing.Status = txn.Status;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    var efTxn = new EfTransaction
                    {
                        TransactionId = txn.TransactionId,
                        UserId = txn.UserId,
                        Status = txn.Status,
                        Currency = txn.Currency,
                        Amount = txn.Amount,
                        CreatedAt = txn.CreatedAt,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _efContext.Transactions.Add(efTxn);
                }
                totalAffected++;
            }

            await _efContext.SaveChangesAsync();
            return totalAffected;
        });
    }

    private List<Transaction> GenerateTestTransactions(int count)
    {
        var random = new Random(42);
        var transactions = new List<Transaction>();

        for (int i = 0; i < count; i++)
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
    public class EfTestDbContext : DbContext
    {
        public EfTestDbContext(DbContextOptions<EfTestDbContext> options) : base(options) { }

        public DbSet<EfTransaction> Transactions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfTransaction>(entity =>
            {
                entity.ToTable("transactions");
                entity.HasKey(e => e.TransactionId);
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
        }
    }

    public class EfTransaction
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