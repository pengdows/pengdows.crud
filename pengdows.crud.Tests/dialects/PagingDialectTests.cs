using System;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Tests for dialect-specific paging SQL generation (SupportsOffsetFetch,
/// SupportsLimitOffset, AppendPaging).
/// </summary>
public class PagingDialectTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly ILogger _log = NullLoggerFactory.Instance.CreateLogger("PagingTest");

    private static SqlServerDialect SqlServer() =>
        new(new fakeDbFactory(SupportedDatabase.SqlServer), _log);

    private static PostgreSqlDialect PostgreSql() =>
        new(new fakeDbFactory(SupportedDatabase.PostgreSql), _log);

    private static SqliteDialect Sqlite() =>
        new(new fakeDbFactory(SupportedDatabase.Sqlite), _log);

    private static OracleDialect Oracle() =>
        new(new fakeDbFactory(SupportedDatabase.Oracle), _log);

    private static MySqlDialect MySql(Version version) =>
        WithVersion(new MySqlDialect(new fakeDbFactory(SupportedDatabase.MySql), _log), version);

    private static MariaDbDialect MariaDb(Version version) =>
        WithVersion(new MariaDbDialect(new fakeDbFactory(SupportedDatabase.MariaDb), _log), version);

    private static FirebirdDialect Firebird() =>
        new(new fakeDbFactory(SupportedDatabase.Firebird), _log);

    private static PostgreSqlDialect CockroachDb() =>
        new(new fakeDbFactory(SupportedDatabase.CockroachDb), _log);

    private static T WithVersion<T>(T dialect, Version version) where T : SqlDialect
    {
        var info = new DatabaseProductInfo
        {
            ParsedVersion = version,
            DatabaseType = dialect.DatabaseType,
            ProductName = dialect.DatabaseType.ToString(),
            ProductVersion = version.ToString()
        };
        var field = typeof(SqlDialect).GetField("_productInfo", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Missing _productInfo field");
        field.SetValue(dialect, info);
        return dialect;
    }

    private static string Paging(ISqlDialect dialect, int offset, int limit)
    {
        var ctx = new DatabaseContext("fake", new fakeDbFactory(dialect.DatabaseType));
        using var sc = ctx.CreateSqlContainer();
        dialect.AppendPaging(sc.Query, offset, limit);
        return sc.Query.ToString();
    }

    // -------------------------------------------------------------------------
    // SupportsOffsetFetch flags
    // -------------------------------------------------------------------------

    [Fact]
    public void SqlServer_SupportsOffsetFetch_IsTrue()
        => Assert.True(SqlServer().SupportsOffsetFetch);

    [Fact]
    public void SqlServer_SupportsLimitOffset_IsFalse()
        => Assert.False(SqlServer().SupportsLimitOffset);

    [Fact]
    public void Sqlite_SupportsOffsetFetch_IsFalse()
        => Assert.False(Sqlite().SupportsOffsetFetch);

    [Fact]
    public void Sqlite_SupportsLimitOffset_IsTrue()
        => Assert.True(Sqlite().SupportsLimitOffset);

    [Fact]
    public void Oracle_SupportsOffsetFetch_IsTrue()
        => Assert.True(Oracle().SupportsOffsetFetch);

    [Fact]
    public void Oracle_SupportsLimitOffset_IsFalse()
        => Assert.False(Oracle().SupportsLimitOffset);

    [Fact]
    public void PostgreSql_SupportsOffsetFetch_IsTrue()
        => Assert.True(PostgreSql().SupportsOffsetFetch);

    [Fact]
    public void PostgreSql_SupportsLimitOffset_IsTrue()
        => Assert.True(PostgreSql().SupportsLimitOffset);

    [Fact]
    public void Firebird_SupportsOffsetFetch_IsTrue()
        => Assert.True(Firebird().SupportsOffsetFetch);

    // MySQL never supports OFFSET/FETCH — it uses LIMIT/OFFSET across all versions
    [Fact]
    public void MySql_NeverSupportsOffsetFetch()
        => Assert.False(MySql(new Version(8, 0, 35)).SupportsOffsetFetch);

    [Fact]
    public void MySql_AlwaysSupportsLimitOffset()
        => Assert.True(MySql(new Version(5, 7, 0)).SupportsLimitOffset);

    // MariaDB version gates
    [Fact]
    public void MariaDb_Pre10_6_SupportsOffsetFetch_IsFalse()
        => Assert.False(MariaDb(new Version(10, 5, 0)).SupportsOffsetFetch);

    [Fact]
    public void MariaDb_10_6_SupportsOffsetFetch_IsTrue()
        => Assert.True(MariaDb(new Version(10, 6, 0)).SupportsOffsetFetch);

    [Fact]
    public void MariaDb_AlwaysSupportsLimitOffset()
        => Assert.True(MariaDb(new Version(10, 5, 0)).SupportsLimitOffset);

    // -------------------------------------------------------------------------
    // AppendPaging SQL output — OFFSET/FETCH dialects
    // -------------------------------------------------------------------------

    [Fact]
    public void SqlServer_AppendPaging_UsesOffsetFetch()
    {
        var sql = Paging(SqlServer(), offset: 20, limit: 10);
        Assert.Equal(" OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY", sql);
    }

    [Fact]
    public void SqlServer_AppendPaging_OffsetZero_StillEmitsOffset()
    {
        var sql = Paging(SqlServer(), offset: 0, limit: 20);
        Assert.Equal(" OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY", sql);
    }

    [Fact]
    public void Oracle_AppendPaging_UsesOffsetFetch()
    {
        var sql = Paging(Oracle(), offset: 10, limit: 5);
        Assert.Equal(" OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY", sql);
    }

    [Fact]
    public void PostgreSql_AppendPaging_UsesOffsetFetch()
    {
        var sql = Paging(PostgreSql(), offset: 0, limit: 50);
        Assert.Equal(" OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY", sql);
    }

    [Fact]
    public void Firebird_AppendPaging_UsesOffsetFetch()
    {
        var sql = Paging(Firebird(), offset: 30, limit: 15);
        Assert.Equal(" OFFSET 30 ROWS FETCH NEXT 15 ROWS ONLY", sql);
    }

    // -------------------------------------------------------------------------
    // AppendPaging SQL output — LIMIT/OFFSET dialects
    // -------------------------------------------------------------------------

    [Fact]
    public void Sqlite_AppendPaging_UsesLimitOffset()
    {
        var sql = Paging(Sqlite(), offset: 20, limit: 10);
        Assert.Equal(" LIMIT 10 OFFSET 20", sql);
    }

    [Fact]
    public void Sqlite_AppendPaging_OffsetZero_OmitsOffset()
    {
        var sql = Paging(Sqlite(), offset: 0, limit: 25);
        Assert.Equal(" LIMIT 25", sql);
    }

    [Fact]
    public void MySql_AppendPaging_AlwaysUsesLimitOffset()
    {
        var sql = Paging(MySql(new Version(8, 0, 35)), offset: 10, limit: 20);
        Assert.Equal(" LIMIT 20 OFFSET 10", sql);
    }

    [Fact]
    public void MariaDb_Pre10_6_AppendPaging_UsesLimitOffset()
    {
        var sql = Paging(MariaDb(new Version(10, 5, 0)), offset: 5, limit: 15);
        Assert.Equal(" LIMIT 15 OFFSET 5", sql);
    }

    [Fact]
    public void MariaDb_10_6_AppendPaging_UsesOffsetFetch()
    {
        var sql = Paging(MariaDb(new Version(10, 6, 0)), offset: 5, limit: 15);
        Assert.Equal(" OFFSET 5 ROWS FETCH NEXT 15 ROWS ONLY", sql);
    }

    // -------------------------------------------------------------------------
    // ISqlContainer integration — appends to real query builder
    // -------------------------------------------------------------------------

    [Fact]
    public void AppendPaging_AppendsToExistingQuery()
    {
        var dialect = SqlServer();
        var ctx = new DatabaseContext("fake", new fakeDbFactory(SupportedDatabase.SqlServer));
        using var sc = ctx.CreateSqlContainer("SELECT id FROM orders ORDER BY created_on DESC");
        dialect.AppendPaging(sc.Query, offset: 10, limit: 5);
        Assert.Equal(
            "SELECT id FROM orders ORDER BY created_on DESC OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY",
            sc.Query.ToString());
    }

    [Fact]
    public void AppendPaging_Sqlite_AppendsToExistingQuery()
    {
        var dialect = Sqlite();
        var ctx = new DatabaseContext("fake", new fakeDbFactory(SupportedDatabase.Sqlite));
        using var sc = ctx.CreateSqlContainer("SELECT id FROM orders ORDER BY created_on DESC");
        dialect.AppendPaging(sc.Query, offset: 10, limit: 5);
        Assert.Equal(
            "SELECT id FROM orders ORDER BY created_on DESC LIMIT 5 OFFSET 10",
            sc.Query.ToString());
    }
}
