using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for the hierarchical ID retrieval system based on GeneratedKeyPlan.
/// Verifies the correct strategy is chosen for each database type according to the hierarchy:
/// 1. Inline RETURNING/OUTPUT (best)
/// 2. Session-scoped functions (safe)
/// 3. Sequence prefetch (Oracle preferred)
/// 4. Correlation token (universal fallback)
/// 5. Natural key lookup (last resort)
/// </summary>
public class GeneratedKeyPlanTests
{
    private readonly ILogger _logger = NullLogger.Instance;

    private fakeDbFactory CreateFactory(SupportedDatabase dbType)
    {
        return new fakeDbFactory(dbType);
    }

    [Fact]
    public void PostgreSql_GetGeneratedKeyPlan_ReturnsReturning()
    {
        var dialect = new PostgreSqlDialect(CreateFactory(SupportedDatabase.PostgreSql), _logger);
        var plan = dialect.GetGeneratedKeyPlan();
        Assert.Equal(GeneratedKeyPlan.Returning, plan);
    }

    [Fact]
    public void SqlServer_GetGeneratedKeyPlan_ReturnsOutputInserted()
    {
        var dialect = new SqlServerDialect(CreateFactory(SupportedDatabase.SqlServer), _logger);
        var plan = dialect.GetGeneratedKeyPlan();
        Assert.Equal(GeneratedKeyPlan.OutputInserted, plan);
    }

    [Fact]
    public void MySql_GetGeneratedKeyPlan_ReturnsSessionScopedFunction()
    {
        var dialect = new MySqlDialect(CreateFactory(SupportedDatabase.MySql), _logger);
        var plan = dialect.GetGeneratedKeyPlan();
        Assert.Equal(GeneratedKeyPlan.SessionScopedFunction, plan);
    }

    [Fact]
    public void MariaDb_GetGeneratedKeyPlan_ReturnsSessionScopedFunction()
    {
        var dialect = new MariaDbDialect(CreateFactory(SupportedDatabase.MariaDb), _logger);
        var plan = dialect.GetGeneratedKeyPlan();
        Assert.Equal(GeneratedKeyPlan.SessionScopedFunction, plan);
    }

    [Fact]
    public void Oracle_GetGeneratedKeyPlan_ReturnsPrefetchSequence()
    {
        var dialect = new OracleDialect(CreateFactory(SupportedDatabase.Oracle), _logger);
        var plan = dialect.GetGeneratedKeyPlan();
        // Oracle supports RETURNING but sequence prefetch is preferred
        Assert.Equal(GeneratedKeyPlan.PrefetchSequence, plan);
    }

    [Fact]
    public void Firebird_GetGeneratedKeyPlan_ReturnsReturning()
    {
        var dialect = new FirebirdDialect(CreateFactory(SupportedDatabase.Firebird), _logger);
        var plan = dialect.GetGeneratedKeyPlan();
        Assert.Equal(GeneratedKeyPlan.Returning, plan);
    }

    [Fact]
    public void DuckDb_GetGeneratedKeyPlan_ReturnsReturning()
    {
        var dialect = new DuckDbDialect(CreateFactory(SupportedDatabase.DuckDB), _logger);
        var plan = dialect.GetGeneratedKeyPlan();
        Assert.Equal(GeneratedKeyPlan.Returning, plan);
    }

    [Fact]
    public void Sqlite_GetGeneratedKeyPlan_ReturnsSessionScopedFunction()
    {
        var dialect = new SqliteDialect(CreateFactory(SupportedDatabase.Sqlite), _logger);
        var plan = dialect.GetGeneratedKeyPlan();
        // SQLite may support RETURNING depending on version, but session function is safer default
        Assert.Equal(GeneratedKeyPlan.SessionScopedFunction, plan);
    }

    [Theory]
    [InlineData("users", "id", "insert_token", ":token")]
    [InlineData("orders", "order_id", "correlation_id", "@token")]
    public void GetCorrelationTokenLookupQuery_GeneratesCorrectSql(
        string tableName, string idColumn, string tokenColumn, string tokenParam)
    {
        var dialect = new PostgreSqlDialect(CreateFactory(SupportedDatabase.PostgreSql), _logger);
        var query = dialect.GetCorrelationTokenLookupQuery(tableName, idColumn, tokenColumn, tokenParam);

        var expected = $"SELECT \"{idColumn}\" FROM \"{tableName}\" WHERE \"{tokenColumn}\" = {tokenParam}";
        Assert.Equal(expected, query);
    }

    [Fact]
    public void GetNaturalKeyLookupQuery_WithValidColumns_GeneratesCorrectSql()
    {
        var dialect = new SqlServerDialect(CreateFactory(SupportedDatabase.SqlServer), _logger);
        var columns = new[] { "email", "username" };
        var parameters = new[] { "@p1", "@p2" };

        var query = dialect.GetNaturalKeyLookupQuery("users", "id", columns, parameters);

        Assert.Contains("SELECT TOP 1 [id]", query);
        Assert.Contains("FROM [users]", query);
        Assert.Contains("WHERE [email] = @p1 AND [username] = @p2", query);
        Assert.Contains("ORDER BY [id] DESC", query);
    }

    [Fact]
    public void GetNaturalKeyLookupQuery_WithMismatchedArrays_ThrowsArgumentException()
    {
        var dialect = new PostgreSqlDialect(CreateFactory(SupportedDatabase.PostgreSql), _logger);
        var columns = new[] { "email", "username" };
        var parameters = new[] { ":p1" }; // Mismatched count

        var ex = Assert.Throws<ArgumentException>(() =>
            dialect.GetNaturalKeyLookupQuery("users", "id", columns, parameters));
        Assert.Contains("Column names and parameter names must have the same count", ex.Message);
    }

    [Fact]
    public void GetNaturalKeyLookupQuery_WithEmptyColumns_ThrowsInvalidOperationException()
    {
        var dialect = new PostgreSqlDialect(CreateFactory(SupportedDatabase.PostgreSql), _logger);
        var columns = Array.Empty<string>();
        var parameters = Array.Empty<string>();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            dialect.GetNaturalKeyLookupQuery("users", "id", columns, parameters));
        Assert.Contains("Natural key lookup requires at least one column", ex.Message);
        Assert.Contains("Consider using correlation token fallback instead", ex.Message);
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql, true)]
    [InlineData(SupportedDatabase.MariaDb, true)]
    [InlineData(SupportedDatabase.Sqlite, true)]
    [InlineData(SupportedDatabase.SqlServer, true)]
    [InlineData(SupportedDatabase.PostgreSql, false)]
    [InlineData(SupportedDatabase.DuckDB, false)]
    [InlineData(SupportedDatabase.Oracle, false)]
    [InlineData(SupportedDatabase.Firebird, false)]
    public void HasSessionScopedLastIdFunction_ReturnsCorrectValue(SupportedDatabase dbType, bool expectedResult)
    {
        var dialect = CreateDialectForType(dbType);
        var result = dialect.HasSessionScopedLastIdFunction();
        Assert.Equal(expectedResult, result);
    }

    private SqlDialect CreateDialectForType(SupportedDatabase dbType)
    {
        return dbType switch
        {
            SupportedDatabase.SqlServer => new SqlServerDialect(CreateFactory(dbType), _logger),
            SupportedDatabase.PostgreSql => new PostgreSqlDialect(CreateFactory(dbType), _logger),
            SupportedDatabase.Sqlite => new SqliteDialect(CreateFactory(dbType), _logger),
            SupportedDatabase.MySql => new MySqlDialect(CreateFactory(dbType), _logger),
            SupportedDatabase.MariaDb => new MariaDbDialect(CreateFactory(dbType), _logger),
            SupportedDatabase.Oracle => new OracleDialect(CreateFactory(dbType), _logger),
            SupportedDatabase.Firebird => new FirebirdDialect(CreateFactory(dbType), _logger),
            SupportedDatabase.DuckDB => new DuckDbDialect(CreateFactory(dbType), _logger),
            _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, "Unsupported database type")
        };
    }

    /// <summary>
    /// Integration test demonstrating the complete hierarchy for all database types
    /// </summary>
    [Fact]
    public void HierarchicalIdRetrieval_DemonstratesCorrectPriority()
    {
        var testCases = new[]
        {
            // Inline RETURNING/OUTPUT (highest priority)
            (SupportedDatabase.PostgreSql, GeneratedKeyPlan.Returning, "RETURNING \"id\""),
            (SupportedDatabase.SqlServer, GeneratedKeyPlan.OutputInserted, "OUTPUT INSERTED.[id]"),
            (SupportedDatabase.Firebird, GeneratedKeyPlan.Returning, "RETURNING \"id\""),
            (SupportedDatabase.DuckDB, GeneratedKeyPlan.Returning, "RETURNING \"id\""),

            // Session-scoped functions (safe fallback)
            (SupportedDatabase.MySql, GeneratedKeyPlan.SessionScopedFunction, "SELECT LAST_INSERT_ID()"),
            (SupportedDatabase.MariaDb, GeneratedKeyPlan.SessionScopedFunction, "SELECT LAST_INSERT_ID()"),
            (SupportedDatabase.Sqlite, GeneratedKeyPlan.SessionScopedFunction, "SELECT last_insert_rowid()"),

            // Sequence prefetch (Oracle preferred)
            (SupportedDatabase.Oracle, GeneratedKeyPlan.PrefetchSequence, null) // No fallback query for prefetch
        };

        foreach (var (dbType, expectedPlan, expectedQuery) in testCases)
        {
            var dialect = CreateDialectForType(dbType);
            var actualPlan = dialect.GetGeneratedKeyPlan();

            Assert.Equal(expectedPlan, actualPlan);

            // Verify the appropriate method works for the chosen plan
            switch (actualPlan)
            {
                case GeneratedKeyPlan.Returning:
                case GeneratedKeyPlan.OutputInserted:
                    Assert.True(dialect.SupportsInsertReturning);
                    var clause = dialect.GetInsertReturningClause("id");
                    Assert.Equal(expectedQuery, clause);
                    break;

                case GeneratedKeyPlan.SessionScopedFunction:
                    Assert.True(dialect.HasSessionScopedLastIdFunction());
                    var query = dialect.GetLastInsertedIdQuery();
                    Assert.Equal(expectedQuery, query);
                    break;

                case GeneratedKeyPlan.PrefetchSequence:
                    // Oracle throws on GetLastInsertedIdQuery as expected
                    Assert.Throws<NotSupportedException>(() => dialect.GetLastInsertedIdQuery());
                    break;

                case GeneratedKeyPlan.CorrelationToken:
                    // Correlation token should always work
                    var tokenQuery = dialect.GetCorrelationTokenLookupQuery("test", "id", "token", ":token");
                    Assert.NotNull(tokenQuery);
                    Assert.Contains("SELECT", tokenQuery);
                    break;
            }
        }
    }

    /// <summary>
    /// Test that validates the hierarchical decision making
    /// </summary>
    [Fact]
    public void GeneratedKeyPlan_RespectsHierarchy()
    {
        // When a database supports RETURNING, it should prefer that over session functions
        var postgresDialect = new PostgreSqlDialect(CreateFactory(SupportedDatabase.PostgreSql), _logger);
        Assert.True(postgresDialect.SupportsInsertReturning);
        Assert.False(postgresDialect.HasSessionScopedLastIdFunction());
        Assert.Equal(GeneratedKeyPlan.Returning, postgresDialect.GetGeneratedKeyPlan());

        // When a database doesn't support RETURNING but has session functions, use those
        var mysqlDialect = new MySqlDialect(CreateFactory(SupportedDatabase.MySql), _logger);
        Assert.False(mysqlDialect.SupportsInsertReturning);
        Assert.True(mysqlDialect.HasSessionScopedLastIdFunction());
        Assert.Equal(GeneratedKeyPlan.SessionScopedFunction, mysqlDialect.GetGeneratedKeyPlan());

        // Oracle is special case - even though it supports RETURNING, sequence prefetch is preferred
        var oracleDialect = new OracleDialect(CreateFactory(SupportedDatabase.Oracle), _logger);
        Assert.True(oracleDialect.SupportsInsertReturning);
        Assert.Equal(GeneratedKeyPlan.PrefetchSequence, oracleDialect.GetGeneratedKeyPlan());
    }
}