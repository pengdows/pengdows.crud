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
/// Proves thesis points #3, #6, and #7:
///   #3 - pengdows can do things EF can't (native PostgreSQL ENUMs, JSONB, arrays, TSVECTOR)
///   #6 - Things BREAK when EF encounters native PostgreSQL types without registration
///   #7 - No fixing - EF has no path to fix these without adding NpgsqlDataSourceBuilder
///         ENUM registration, HasPostgresEnum, HasColumnType("jsonb"), HasConversion, etc.
///
/// EF is deliberately configured WITHOUT any PostgreSQL-specific configuration.
/// It attempts each operation and fails visibly, printing [BROKEN] messages.
/// pengdows and Dapper handle native PostgreSQL types with zero special configuration.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 3, invocationCount: 10)]
public class DatabaseFeatureBenchmarks : IAsyncDisposable
{
    private const string ComplexQuerySqlTemplate = """
        SELECT t.transaction_id, t.user_id, t.status::text AS status,
               t.currency::text AS currency, t.amount,
               t.metadata->>'risk_score' AS risk_score,
               t.created_at, t.updated_at
        FROM transactions t
        WHERE t.status = {status}::transaction_status
          AND t.metadata->>'risk_score' IS NOT NULL
          AND {tag} = ANY(t.tags)
        ORDER BY t.amount DESC
        LIMIT 100
        """;

    private const string FullTextSearchSqlTemplate = """
        SELECT t.status::text AS status, COUNT(*) AS match_count,
               AVG(ts_rank(t.search_vector, plainto_tsquery('english', {searchTerm}))) AS avg_rank
        FROM transactions t
        WHERE t.search_vector @@ plainto_tsquery('english', {searchTerm2})
        GROUP BY t.status
        ORDER BY avg_rank DESC
        """;

    private const string BulkUpsertSqlTemplate = """
        INSERT INTO transactions (transaction_id, user_id, status, currency, amount, metadata, tags, created_at, updated_at)
        VALUES ({id}, {userId}, {status}::transaction_status, {currency}::currency_code,
                {amount}, {metadata}::jsonb,
                ARRAY[{tag1}, {tag2}]::text[],
                NOW(), NOW())
        ON CONFLICT (transaction_id) DO UPDATE SET
            amount = EXCLUDED.amount,
            metadata = EXCLUDED.metadata,
            tags = EXCLUDED.tags,
            updated_at = NOW()
        """;

    private const string JsonbQuerySqlTemplate = """
        SELECT t.transaction_id, t.user_id, t.status::text AS status,
               t.currency::text AS currency, t.amount,
               t.metadata->>'risk_score' AS risk_score,
               t.created_at, t.updated_at
        FROM transactions t
        WHERE (t.metadata->>'risk_score')::int > {minScore}
        ORDER BY (t.metadata->>'risk_score')::int DESC
        LIMIT 50
        """;

    private const string ArrayContainsSqlTemplate = """
        SELECT t.transaction_id, t.user_id, t.status::text AS status,
               t.currency::text AS currency, t.amount,
               t.created_at, t.updated_at
        FROM transactions t
        WHERE t.tags @> ARRAY[{tag}]::text[]
        ORDER BY t.created_at DESC
        LIMIT 50
        """;

    private IContainer? _container;
    private string _baseConnStr = string.Empty;

    private IDatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;
    private NpgsqlDataSource _pengdowsDataSource = null!;
    private NpgsqlDataSource _dapperDataSource = null!;
    private DbContextOptions<EfBrokenDbContext> _efOptions = null!;
    private EfBrokenDbContext _efContext = null!;

    private readonly List<long> _transactionIds = new();

    [Params(5000)] public int TransactionCount;
    [Params(16)] public int Parallelism;
    [Params(64)] public int OperationsPerRun;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithEnvironment("POSTGRES_DB", "dbfeatures_test")
            .WithPortBinding(0, 5432)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _container.StartAsync();

        var mappedPort = _container.GetMappedPublicPort(5432);
        _baseConnStr =
            $"Host=localhost;Port={mappedPort};Database=dbfeatures_test;Username=postgres;Password=postgres";

        await WaitForReady();
        await CreateSchemaAndSeedAsync();

        // Setup pengdows.crud with NpgsqlDataSource and separate Application Name
        var pengdowsConnStr = _baseConnStr + ";Application Name=pengdows_bench";
        _pengdowsDataSource = NpgsqlDataSource.Create(pengdowsConnStr);
        _map = new TypeMapRegistry();
        _map.Register<PgTransaction>();
        _map.Register<TransactionRow>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = pengdowsConnStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsContext = new DatabaseContext(cfg, _pengdowsDataSource, NpgsqlFactory.Instance);

        // Setup Dapper with NpgsqlDataSource and separate Application Name
        var dapperConnStr = _baseConnStr + ";Application Name=dapper_bench";
        _dapperDataSource = NpgsqlDataSource.Create(dapperConnStr);

        // Setup Entity Framework - DELIBERATELY BROKEN (plain UseNpgsql, NO NpgsqlDataSourceBuilder)
        var efConnStr = _baseConnStr + ";Application Name=ef_bench";
        _efOptions = new DbContextOptionsBuilder<EfBrokenDbContext>()
            .UseNpgsql(efConnStr)
            .Options;
        _efContext = new EfBrokenDbContext(_efOptions);

        Console.WriteLine("[BENCHMARK] DatabaseFeatureBenchmarks: PostgreSQL ENUMs, JSONB, arrays, TSVECTOR");
        Console.WriteLine($"[BENCHMARK] Seeded {TransactionCount} transactions");
        Console.WriteLine("[BENCHMARK] EF has NO PostgreSQL-specific configuration (will fail on native types)");
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

        await conn.ExecuteAsync(@"
            DROP TABLE IF EXISTS transactions;
            DROP TYPE IF EXISTS transaction_status;
            DROP TYPE IF EXISTS currency_code;

            -- PostgreSQL-specific: Custom ENUM types
            CREATE TYPE transaction_status AS ENUM ('pending', 'completed', 'failed', 'refunded', 'disputed');
            CREATE TYPE currency_code AS ENUM ('USD', 'EUR', 'GBP', 'JPY', 'CAD', 'AUD');

            CREATE TABLE transactions (
                transaction_id BIGINT PRIMARY KEY,
                user_id INTEGER NOT NULL,
                status transaction_status NOT NULL DEFAULT 'pending',
                currency currency_code NOT NULL DEFAULT 'USD',
                amount DECIMAL(18,2) NOT NULL,
                -- PostgreSQL-specific: JSONB for flexible metadata
                metadata JSONB DEFAULT '{}',
                -- PostgreSQL-specific: ARRAY column for tags
                tags TEXT[] DEFAULT '{}',
                created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                -- PostgreSQL-specific: Full-text search vector
                search_vector TSVECTOR
            );

            -- PostgreSQL-specific: GIN index for JSONB
            CREATE INDEX idx_transactions_metadata_gin ON transactions USING GIN (metadata);

            -- PostgreSQL-specific: GIN index for text search
            CREATE INDEX idx_transactions_search_gin ON transactions USING GIN (search_vector);

            -- PostgreSQL-specific: GIN index for array containment
            CREATE INDEX idx_transactions_tags_gin ON transactions USING GIN (tags);
            ", transaction: tx);

        // Seed transactions with realistic data
        var random = new Random(42);
        var statuses = new[] { "pending", "completed", "failed", "refunded", "disputed" };
        var currencies = new[] { "USD", "EUR", "GBP", "JPY", "CAD", "AUD" };
        var tagPool = new[] { "high-value", "recurring", "international", "flagged", "priority", "bulk", "retail", "wholesale" };

        for (var i = 1; i <= TransactionCount; i++)
        {
            var status = statuses[random.Next(statuses.Length)];
            var currency = currencies[random.Next(currencies.Length)];
            var amount = Math.Round((decimal)(random.NextDouble() * 10000), 2);
            var riskScore = random.Next(1, 100);
            var tags = tagPool.OrderBy(_ => random.Next()).Take(random.Next(1, 4)).ToArray();

            var metadata = $$"""
                {"risk_score": {{riskScore}}, "source": "api", "version": "{{random.Next(1, 5)}}"}
                """;

            var description = $"Transaction {i:D5} for user {random.Next(1, 500)}";

            var transactionId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO transactions (transaction_id, user_id, status, currency, amount, metadata, tags, search_vector, created_at, updated_at)
                VALUES (@id, @userId, @status::transaction_status, @currency::currency_code,
                        @amount, @metadata::jsonb, @tags,
                        to_tsvector('english', @description),
                        NOW() - interval '1 day' * @daysAgo, NOW())
                RETURNING transaction_id",
                new
                {
                    id = (long)i,
                    userId = random.Next(1, 500),
                    status,
                    currency,
                    amount,
                    metadata,
                    tags,
                    description,
                    daysAgo = random.Next(0, 365)
                }, tx);
            _transactionIds.Add(transactionId);
        }

        await tx.CommitAsync();
        await conn.ExecuteAsync("ANALYZE transactions;");
    }

    // ========================================================================
    // Sequential: ComplexQuery
    // ========================================================================

    [Benchmark]
    public async Task<List<TransactionRow>> ComplexQuery_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildComplexQuerySql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("status", DbType.String, "completed");
        container.AddParameterWithValue("tag", DbType.String, "high-value");

        var helper = new TableGateway<TransactionRow, long>(_pengdowsContext);
        return await helper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperTransactionRow>> ComplexQuery_Dapper()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildComplexQuerySql(param => $"@{param}");
        var results = await conn.QueryAsync<DapperTransactionRow>(sql,
            new { status = "completed", tag = "high-value" });
        return results.ToList();
    }

    [Benchmark]
    public async Task<List<EfTransactionRow>> ComplexQuery_EntityFramework()
    {
        try
        {
            await using var ctx = new EfBrokenDbContext(_efOptions);
            var sql = BuildComplexQuerySql(param => $"@{param}");
            return await ctx.TransactionRows
                .FromSqlRaw(sql,
                    new NpgsqlParameter("status", "completed"),
                    new NpgsqlParameter("tag", "high-value"))
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF ComplexQuery: {ex.GetType().Name}: {ex.Message}");
            return new List<EfTransactionRow>();
        }
    }

    // ========================================================================
    // Sequential: FullTextSearch
    // ========================================================================

    [Benchmark]
    public async Task<List<FullTextSearchRow>> FullTextSearch_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildFullTextSearchSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("searchTerm", DbType.String, "transaction");
        container.AddParameterWithValue("searchTerm2", DbType.String, "transaction");

        var helper = new TableGateway<FullTextSearchRow, string>(_pengdowsContext);
        return await helper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperFullTextSearchRow>> FullTextSearch_Dapper()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildFullTextSearchSql(param => $"@{param}");
        var results = await conn.QueryAsync<DapperFullTextSearchRow>(sql,
            new { searchTerm = "transaction", searchTerm2 = "transaction" });
        return results.ToList();
    }

    [Benchmark]
    public async Task<List<EfFullTextSearchRow>> FullTextSearch_EntityFramework()
    {
        try
        {
            await using var ctx = new EfBrokenDbContext(_efOptions);
            var sql = BuildFullTextSearchSql(param => $"@{param}");
            return await ctx.FullTextSearchRows
                .FromSqlRaw(sql,
                    new NpgsqlParameter("searchTerm", "transaction"),
                    new NpgsqlParameter("searchTerm2", "transaction"))
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF FullTextSearch: {ex.GetType().Name}: {ex.Message}");
            return new List<EfFullTextSearchRow>();
        }
    }

    // ========================================================================
    // Sequential: BulkUpsert
    // ========================================================================

    [Benchmark]
    public async Task<int> BulkUpsert_Pengdows()
    {
        var totalAffected = 0;
        for (var i = 0; i < 10; i++)
        {
            var id = _transactionIds[i];
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildBulkUpsertSql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("id", DbType.Int64, id);
            container.AddParameterWithValue("userId", DbType.Int32, 999);
            container.AddParameterWithValue("status", DbType.String, "completed");
            container.AddParameterWithValue("currency", DbType.String, "USD");
            container.AddParameterWithValue("amount", DbType.Decimal, 12345.67m);
            container.AddParameterWithValue("metadata", DbType.String, """{"risk_score": 5, "source": "bench"}""");
            container.AddParameterWithValue("tag1", DbType.String, "benchmark");
            container.AddParameterWithValue("tag2", DbType.String, "upsert");
            totalAffected += await container.ExecuteNonQueryAsync();
        }

        return totalAffected;
    }

    [Benchmark]
    public async Task<int> BulkUpsert_Dapper()
    {
        var totalAffected = 0;
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        for (var i = 0; i < 10; i++)
        {
            var id = _transactionIds[i];
            var sql = BuildBulkUpsertSql(param => $"@{param}");
            totalAffected += await conn.ExecuteAsync(sql, new
            {
                id,
                userId = 999,
                status = "completed",
                currency = "USD",
                amount = 12345.67m,
                metadata = """{"risk_score": 5, "source": "bench"}""",
                tag1 = "benchmark",
                tag2 = "upsert"
            });
        }

        return totalAffected;
    }

    [Benchmark]
    public async Task<int> BulkUpsert_EntityFramework()
    {
        try
        {
            var totalAffected = 0;
            await using var ctx = new EfBrokenDbContext(_efOptions);
            for (var i = 0; i < 10; i++)
            {
                var id = _transactionIds[i];
                var sql = BuildBulkUpsertSql(param => $"@{param}");
                totalAffected += await ctx.Database.ExecuteSqlRawAsync(sql,
                    new NpgsqlParameter("id", id),
                    new NpgsqlParameter("userId", 999),
                    new NpgsqlParameter("status", "completed"),
                    new NpgsqlParameter("currency", "USD"),
                    new NpgsqlParameter("amount", 12345.67m),
                    new NpgsqlParameter("metadata", """{"risk_score": 5, "source": "bench"}"""),
                    new NpgsqlParameter("tag1", "benchmark"),
                    new NpgsqlParameter("tag2", "upsert"));
            }

            return totalAffected;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF BulkUpsert: {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    // ========================================================================
    // Sequential: JsonbQuery
    // ========================================================================

    [Benchmark]
    public async Task<List<TransactionRow>> JsonbQuery_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildJsonbQuerySql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("minScore", DbType.Int32, 80);

        var helper = new TableGateway<TransactionRow, long>(_pengdowsContext);
        return await helper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperTransactionRow>> JsonbQuery_Dapper()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildJsonbQuerySql(param => $"@{param}");
        var results = await conn.QueryAsync<DapperTransactionRow>(sql, new { minScore = 80 });
        return results.ToList();
    }

    [Benchmark]
    public async Task<List<EfTransactionRow>> JsonbQuery_EntityFramework()
    {
        try
        {
            await using var ctx = new EfBrokenDbContext(_efOptions);
            var sql = BuildJsonbQuerySql(param => $"@{param}");
            return await ctx.TransactionRows
                .FromSqlRaw(sql, new NpgsqlParameter("minScore", 80))
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF JsonbQuery: {ex.GetType().Name}: {ex.Message}");
            return new List<EfTransactionRow>();
        }
    }

    // ========================================================================
    // Sequential: ArrayContains
    // ========================================================================

    [Benchmark]
    public async Task<List<TransactionRow>> ArrayContains_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildArrayContainsSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("tag", DbType.String, "flagged");

        var helper = new TableGateway<TransactionRow, long>(_pengdowsContext);
        return await helper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperTransactionRow>> ArrayContains_Dapper()
    {
        await using var conn = await _dapperDataSource.OpenConnectionAsync();
        var sql = BuildArrayContainsSql(param => $"@{param}");
        var results = await conn.QueryAsync<DapperTransactionRow>(sql, new { tag = "flagged" });
        return results.ToList();
    }

    [Benchmark]
    public async Task<List<EfTransactionRow>> ArrayContains_EntityFramework()
    {
        try
        {
            await using var ctx = new EfBrokenDbContext(_efOptions);
            var sql = BuildArrayContainsSql(param => $"@{param}");
            return await ctx.TransactionRows
                .FromSqlRaw(sql, new NpgsqlParameter("tag", "flagged"))
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF ArrayContains: {ex.GetType().Name}: {ex.Message}");
            return new List<EfTransactionRow>();
        }
    }

    // ========================================================================
    // Concurrent: ComplexQuery
    // ========================================================================

    [Benchmark]
    public async Task ComplexQuery_Pengdows_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildComplexQuerySql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("status", DbType.String, "completed");
            container.AddParameterWithValue("tag", DbType.String, "high-value");
            var helper = new TableGateway<TransactionRow, long>(_pengdowsContext);
            await helper.LoadListAsync(container);
        });
    }

    [Benchmark]
    public async Task ComplexQuery_Dapper_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            var sql = BuildComplexQuerySql(param => $"@{param}");
            var results = await conn.QueryAsync<DapperTransactionRow>(sql,
                new { status = "completed", tag = "high-value" });
            results.ToList();
        });
    }

    [Benchmark]
    public async Task ComplexQuery_EntityFramework_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new EfBrokenDbContext(_efOptions);
            var sql = BuildComplexQuerySql(param => $"@{param}");
            await ctx.TransactionRows
                .FromSqlRaw(sql,
                    new NpgsqlParameter("status", "completed"),
                    new NpgsqlParameter("tag", "high-value"))
                .AsNoTracking()
                .ToListAsync();
        }, ex => Console.WriteLine($"[BROKEN] EF ComplexQuery_Concurrent: {ex.GetType().Name}: {ex.Message}"));
    }

    // ========================================================================
    // Concurrent: FullTextSearch
    // ========================================================================

    [Benchmark]
    public async Task FullTextSearch_Pengdows_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildFullTextSearchSql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("searchTerm", DbType.String, "transaction");
            container.AddParameterWithValue("searchTerm2", DbType.String, "transaction");
            var helper = new TableGateway<FullTextSearchRow, string>(_pengdowsContext);
            await helper.LoadListAsync(container);
        });
    }

    [Benchmark]
    public async Task FullTextSearch_Dapper_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            var sql = BuildFullTextSearchSql(param => $"@{param}");
            var results = await conn.QueryAsync<DapperFullTextSearchRow>(sql,
                new { searchTerm = "transaction", searchTerm2 = "transaction" });
            results.ToList();
        });
    }

    [Benchmark]
    public async Task FullTextSearch_EntityFramework_Concurrent()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            await using var ctx = new EfBrokenDbContext(_efOptions);
            var sql = BuildFullTextSearchSql(param => $"@{param}");
            await ctx.FullTextSearchRows
                .FromSqlRaw(sql,
                    new NpgsqlParameter("searchTerm", "transaction"),
                    new NpgsqlParameter("searchTerm2", "transaction"))
                .AsNoTracking()
                .ToListAsync();
        }, ex => Console.WriteLine($"[BROKEN] EF FullTextSearch_Concurrent: {ex.GetType().Name}: {ex.Message}"));
    }

    // ========================================================================
    // Concurrent: BulkUpsert
    // ========================================================================

    [Benchmark]
    public async Task BulkUpsert_Pengdows_Concurrent()
    {
        var counter = 0;
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            var idx = Interlocked.Increment(ref counter) % _transactionIds.Count;
            var id = _transactionIds[idx];
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildBulkUpsertSql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("id", DbType.Int64, id);
            container.AddParameterWithValue("userId", DbType.Int32, 999);
            container.AddParameterWithValue("status", DbType.String, "completed");
            container.AddParameterWithValue("currency", DbType.String, "USD");
            container.AddParameterWithValue("amount", DbType.Decimal, 12345.67m);
            container.AddParameterWithValue("metadata", DbType.String, """{"risk_score": 5, "source": "bench"}""");
            container.AddParameterWithValue("tag1", DbType.String, "benchmark");
            container.AddParameterWithValue("tag2", DbType.String, "upsert");
            await container.ExecuteNonQueryAsync();
        });
    }

    [Benchmark]
    public async Task BulkUpsert_Dapper_Concurrent()
    {
        var counter = 0;
        await BenchmarkConcurrency.RunConcurrent(OperationsPerRun, Parallelism, async () =>
        {
            var idx = Interlocked.Increment(ref counter) % _transactionIds.Count;
            var id = _transactionIds[idx];
            await using var conn = await _dapperDataSource.OpenConnectionAsync();
            var sql = BuildBulkUpsertSql(param => $"@{param}");
            await conn.ExecuteAsync(sql, new
            {
                id,
                userId = 999,
                status = "completed",
                currency = "USD",
                amount = 12345.67m,
                metadata = """{"risk_score": 5, "source": "bench"}""",
                tag1 = "benchmark",
                tag2 = "upsert"
            });
        });
    }

    [Benchmark]
    public async Task BulkUpsert_EntityFramework_Concurrent()
    {
        var counter = 0;
        await BenchmarkConcurrency.RunConcurrentWithErrors(OperationsPerRun, Parallelism, async () =>
        {
            var idx = Interlocked.Increment(ref counter) % _transactionIds.Count;
            var id = _transactionIds[idx];
            await using var ctx = new EfBrokenDbContext(_efOptions);
            var sql = BuildBulkUpsertSql(param => $"@{param}");
            await ctx.Database.ExecuteSqlRawAsync(sql,
                new NpgsqlParameter("id", id),
                new NpgsqlParameter("userId", 999),
                new NpgsqlParameter("status", "completed"),
                new NpgsqlParameter("currency", "USD"),
                new NpgsqlParameter("amount", 12345.67m),
                new NpgsqlParameter("metadata", """{"risk_score": 5, "source": "bench"}"""),
                new NpgsqlParameter("tag1", "benchmark"),
                new NpgsqlParameter("tag2", "upsert"));
        }, ex => Console.WriteLine($"[BROKEN] EF BulkUpsert_Concurrent: {ex.GetType().Name}: {ex.Message}"));
    }

    // ========================================================================
    // SQL Builders
    // ========================================================================

    private static string BuildComplexQuerySql(Func<string, string> param)
    {
        return ComplexQuerySqlTemplate
            .Replace("{status}", param("status"))
            .Replace("{tag}", param("tag"));
    }

    private static string BuildFullTextSearchSql(Func<string, string> param)
    {
        return FullTextSearchSqlTemplate
            .Replace("{searchTerm2}", param("searchTerm2"))
            .Replace("{searchTerm}", param("searchTerm"));
    }

    private static string BuildBulkUpsertSql(Func<string, string> param)
    {
        return BulkUpsertSqlTemplate
            .Replace("{id}", param("id"))
            .Replace("{userId}", param("userId"))
            .Replace("{status}", param("status"))
            .Replace("{currency}", param("currency"))
            .Replace("{amount}", param("amount"))
            .Replace("{metadata}", param("metadata"))
            .Replace("{tag1}", param("tag1"))
            .Replace("{tag2}", param("tag2"));
    }

    private static string BuildJsonbQuerySql(Func<string, string> param)
    {
        return JsonbQuerySqlTemplate
            .Replace("{minScore}", param("minScore"));
    }

    private static string BuildArrayContainsSql(Func<string, string> param)
    {
        return ArrayContainsSqlTemplate
            .Replace("{tag}", param("tag"));
    }

    // ========================================================================
    // Cleanup
    // ========================================================================

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_pengdowsContext is IAsyncDisposable pad)
        {
            await pad.DisposeAsync();
        }

        if (_pengdowsDataSource != null!)
        {
            await _pengdowsDataSource.DisposeAsync();
        }

        if (_efContext != null!)
        {
            await _efContext.DisposeAsync();
        }

        if (_dapperDataSource != null!)
        {
            await _dapperDataSource.DisposeAsync();
        }

        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
    }

    // ========================================================================
    // pengdows.crud entity
    // ========================================================================

    [Table("transactions")]
    public class PgTransaction
    {
        [Id(true)]
        [Column("transaction_id", DbType.Int64)]
        public long TransactionId { get; set; }

        [Column("user_id", DbType.Int32)]
        public int UserId { get; set; }

        [Column("status", DbType.String)]
        public string Status { get; set; } = string.Empty;

        [Column("currency", DbType.String)]
        public string Currency { get; set; } = string.Empty;

        [Column("amount", DbType.Decimal)]
        public decimal Amount { get; set; }

        [Column("metadata", DbType.Object)]
        [Json]
        public string? Metadata { get; set; }

        [Column("tags", DbType.Object)]
        public string[]? Tags { get; set; }

        [Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at", DbType.DateTime)]
        public DateTime UpdatedAt { get; set; }

        [Column("search_vector", DbType.Object)]
        public string? SearchVector { get; set; }
    }

    /// <summary>
    /// Projection entity for complex query and JSONB query results (pengdows).
    /// </summary>
    [Table("transactions")]
    public class TransactionRow
    {
        [Id(true)]
        [Column("transaction_id", DbType.Int64)]
        public long TransactionId { get; set; }

        [Column("user_id", DbType.Int32)]
        public int UserId { get; set; }

        [Column("status", DbType.String)]
        public string Status { get; set; } = string.Empty;

        [Column("currency", DbType.String)]
        public string Currency { get; set; } = string.Empty;

        [Column("amount", DbType.Decimal)]
        public decimal Amount { get; set; }

        [Column("risk_score", DbType.String)]
        public string? RiskScore { get; set; }

        [Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at", DbType.DateTime)]
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Projection entity for full-text search aggregation results (pengdows).
    /// </summary>
    [Table("transactions")]
    public class FullTextSearchRow
    {
        [Id(true)]
        [Column("status", DbType.String)]
        public string Status { get; set; } = string.Empty;

        [Column("match_count", DbType.Int64)]
        public long MatchCount { get; set; }

        [Column("avg_rank", DbType.Double)]
        public double AvgRank { get; set; }
    }

    // ========================================================================
    // Dapper row types
    // ========================================================================

    public class DapperTransactionRow
    {
        public long TransactionId { get; set; }
        public int UserId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? RiskScore { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class DapperFullTextSearchRow
    {
        public string Status { get; set; } = string.Empty;
        public long MatchCount { get; set; }
        public double AvgRank { get; set; }
    }

    // ========================================================================
    // EF entity and context - DELIBERATELY BROKEN
    // ========================================================================

    /// <summary>
    /// EF entity for transactions. Uses string types for status/currency (NOT enum types),
    /// string for metadata (NOT jsonb), string[] for tags. No PostgreSQL-specific configuration.
    /// </summary>
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
        // NO SearchVector property - EF cannot map TSVECTOR
    }

    /// <summary>
    /// Keyless EF entity for complex query and JSONB query FromSqlRaw results.
    /// </summary>
    public class EfTransactionRow
    {
        public long TransactionId { get; set; }
        public int UserId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? RiskScore { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Keyless EF entity for full-text search aggregation FromSqlRaw results.
    /// </summary>
    public class EfFullTextSearchRow
    {
        public string Status { get; set; } = string.Empty;
        public long MatchCount { get; set; }
        public double AvgRank { get; set; }
    }

    /// <summary>
    /// EF DbContext - DELIBERATELY BROKEN.
    /// NO HasPostgresEnum, NO HasColumnType("jsonb"), NO HasColumnType("text[]"),
    /// NO HasConversion for ENUMs, NO NpgsqlDataSourceBuilder ENUM registration.
    /// NO search_vector mapping at all.
    /// Just basic ToTable + HasKey + HasColumnName mappings.
    /// </summary>
    public class EfBrokenDbContext : DbContext
    {
        public EfBrokenDbContext(DbContextOptions<EfBrokenDbContext> options) : base(options)
        {
        }

        public DbSet<EfTransaction> Transactions { get; set; }
        public DbSet<EfTransactionRow> TransactionRows { get; set; }
        public DbSet<EfFullTextSearchRow> FullTextSearchRows { get; set; }

        // NO HasPostgresEnum, NO HasColumnType, NO HasConversion
        // Just basic ToTable + HasKey + HasColumnName mappings
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
                // NO search_vector mapping at all
            });

            modelBuilder.Entity<EfTransactionRow>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
                entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.Status).HasColumnName("status");
                entity.Property(e => e.Currency).HasColumnName("currency");
                entity.Property(e => e.Amount).HasColumnName("amount");
                entity.Property(e => e.RiskScore).HasColumnName("risk_score");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<EfFullTextSearchRow>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
                entity.Property(e => e.Status).HasColumnName("status");
                entity.Property(e => e.MatchCount).HasColumnName("match_count");
                entity.Property(e => e.AvgRank).HasColumnName("avg_rank");
            });
        }
    }
}
