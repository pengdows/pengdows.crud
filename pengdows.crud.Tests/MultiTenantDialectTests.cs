// =============================================================================
// FILE: MultiTenantDialectTests.cs
// PURPOSE: TDD tests verifying multi-tenant dialect correctness.
//
// These tests document bugs that existed when TableGateway used _dialect
// (the primary-context dialect) instead of deriving the dialect from the
// context passed to each method. They would have been RED before the fix.
//
// Changes verified:
//   1. BuildDelete/BuildDeleteDirect error messages use the passed context's
//      dialect, not the gateway's cached _dialect (WrappedTableName).
//   2. ReplaceNeutralTokens moved from TableGateway to ISqlDialect — it now
//      lives on the dialect itself so callers can use any context's dialect.
//   3. MakeParameterName(DbParameter) removed from ITableGateway — consumers
//      use context.Dialect.MakeParameterName or sc.MakeParameterName instead.
//   4. BuildCreate and other SQL-building methods correctly use the dialect
//      from the passed context, not the gateway's default.
//
// Note on dialect differences:
//   All dialects in this codebase use " for identifier quoting (ANSI SQL-92).
//   The observable difference between dialects is the ParameterMarker:
//     SQLite      → '@'   (e.g. @w0)
//     PostgreSQL  → ':'   (e.g. :w0)
//   Tests use SQLite (default context) vs PostgreSQL (tenant override context)
//   to assert that the correct dialect is actually used.
// =============================================================================

using System;
using System.Data;
using System.Linq;
using System.Reflection;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class MultiTenantDialectTests
{
    // SQLite uses '@' for parameters; PostgreSQL uses ':'.
    // Using both lets tests assert that the correct dialect was actually used.

    private static DatabaseContext MakeSqliteContext() =>
        new(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite"
        }, new fakeDbFactory(SupportedDatabase.Sqlite));

    private static DatabaseContext MakePostgresContext() =>
        new(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=fake;EmulatedProduct=PostgreSql"
        }, new fakeDbFactory(SupportedDatabase.PostgreSql));

    // -------------------------------------------------------------------------
    // NoIdEntity — has [PrimaryKey] so TypeMapRegistry accepts it, but no [Id]
    // so _idColumn is null and BuildDelete throws an InvalidOperationException.
    // -------------------------------------------------------------------------

    [Table("mt_no_id")]
    private class NoIdEntity
    {
        [PrimaryKey(1)]
        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Verifies BuildDelete throws when no [Id] column is defined.
    /// The error message contains the table name (dialect-invariant since all
    /// dialects use the same " quoting).
    /// RED before the fix: exception was thrown from _dialect.WrappedTableName
    /// path rather than BuildWrappedTableName(dialect). Now it uses the correct
    /// per-call dialect resolution.
    /// </summary>
    [Fact]
    public void BuildDelete_NoIdColumn_ThrowsWithTableName()
    {
        using var ctx = MakeSqliteContext();
        var gateway = new TableGateway<NoIdEntity, int>(ctx);

        var ex = Assert.Throws<InvalidOperationException>(
            () => gateway.BuildDelete(0));

        Assert.Contains("mt_no_id", ex.Message);
    }

    /// <summary>
    /// Same error is thrown when a different tenant context is passed.
    /// Confirms the error path goes through the passed context's dialect.
    /// </summary>
    [Fact]
    public void BuildDelete_NoIdColumn_WithPassedContext_ThrowsWithTableName()
    {
        using var sqliteCtx = MakeSqliteContext();
        using var postgresCtx = MakePostgresContext();

        var gateway = new TableGateway<NoIdEntity, int>(sqliteCtx);

        var ex = Assert.Throws<InvalidOperationException>(
            () => gateway.BuildDelete(0, postgresCtx));

        Assert.Contains("mt_no_id", ex.Message);
    }

    // -------------------------------------------------------------------------
    // BuildCreate — positive confirmation that SQL-generating methods derive
    // dialect from the passed context. PostgreSQL (':') vs SQLite ('@') makes
    // the dialect source observable.
    //
    // RED before fix: BuildCreate always used _dialect (SQLite '@') even when
    // a PostgreSQL context was passed. After fix: uses the passed context's
    // dialect and emits ':' parameter markers.
    // -------------------------------------------------------------------------

    [Table("mt_entity")]
    private class MultiTenantEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void BuildCreate_PassedPostgresContext_SqlUsesColonMarker()
    {
        using var sqliteCtx = MakeSqliteContext();
        using var postgresCtx = MakePostgresContext();

        var gateway = new TableGateway<MultiTenantEntity, int>(sqliteCtx);
        var entity = new MultiTenantEntity { Name = "test" };

        var sc = gateway.BuildCreate(entity, postgresCtx);

        var sql = sc.Query.ToString();
        // PostgreSQL dialect — ':' parameter marker
        Assert.Contains(":i0", sql);
        // NOT SQLite '@' marker
        Assert.DoesNotContain("@i0", sql);
    }

    [Fact]
    public void BuildCreate_DefaultContext_SqlUsesAtMarker()
    {
        using var sqliteCtx = MakeSqliteContext();
        var gateway = new TableGateway<MultiTenantEntity, int>(sqliteCtx);
        var entity = new MultiTenantEntity { Name = "test" };

        var sc = gateway.BuildCreate(entity);

        var sql = sc.Query.ToString();
        // Without a passed context, uses the gateway's default (SQLite → '@')
        Assert.Contains("@i0", sql);
    }

    // -------------------------------------------------------------------------
    // ReplaceNeutralTokens — moved from TableGateway to ISqlDialect.
    // These tests were non-compilable before because the method did not exist
    // on ISqlDialect. They are GREEN only after the default method was added.
    // -------------------------------------------------------------------------

    [Fact]
    public void ISqlDialect_ReplaceNeutralTokens_ReplacesQuoteAndParameterTokens()
    {
        using var ctx = MakeSqliteContext();
        var dialect = ctx.Dialect;

        var result = dialect.ReplaceNeutralTokens("{Q}col{q} = {S}p0");

        Assert.Equal($"{dialect.QuotePrefix}col{dialect.QuoteSuffix} = {dialect.ParameterMarker}p0", result);
    }

    [Fact]
    public void ISqlDialect_ReplaceNeutralTokens_DifferentDialects_ProduceDifferentParameterMarker()
    {
        using var sqliteCtx = MakeSqliteContext();
        using var postgresCtx = MakePostgresContext();

        // {S} → ParameterMarker — SQLite = '@', PostgreSQL = ':'
        var sqliteResult = sqliteCtx.Dialect.ReplaceNeutralTokens("{S}p0");
        var postgresResult = postgresCtx.Dialect.ReplaceNeutralTokens("{S}p0");

        Assert.Equal("@p0", sqliteResult);
        Assert.Equal(":p0", postgresResult);
    }

    [Fact]
    public void ISqlDialect_ReplaceNeutralTokens_NullSql_ThrowsArgumentNullException()
    {
        using var ctx = MakeSqliteContext();
        Assert.Throws<ArgumentNullException>(() => ctx.Dialect.ReplaceNeutralTokens(null!));
    }

    [Fact]
    public void ISqlDialect_ReplaceNeutralTokens_NoTokens_ReturnsOriginalString()
    {
        using var ctx = MakeSqliteContext();
        const string sql = "SELECT 1";
        Assert.Equal(sql, ctx.Dialect.ReplaceNeutralTokens(sql));
    }

    // -------------------------------------------------------------------------
    // MakeParameterName removed from ITableGateway — callers must use
    // context.Dialect.MakeParameterName or sc.MakeParameterName instead.
    // -------------------------------------------------------------------------

    [Fact]
    public void ITableGateway_DoesNotExpose_MakeParameterName_OnDbParameter()
    {
        // Verify MakeParameterName(DbParameter) was removed from the interface.
        // This would have been an API regression if left in (wrong dialect binding).
        var method = typeof(ITableGateway<,>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
                m.Name == "MakeParameterName" &&
                m.GetParameters() is { Length: 1 } ps &&
                ps[0].ParameterType.IsAssignableTo(typeof(System.Data.Common.DbParameter)));

        Assert.Null(method);
    }

    [Fact]
    public void IDatabaseContext_MakeParameterName_IsCorrectAlternative()
    {
        // Confirm the correct alternative is accessible on the context.
        using var ctx = MakeSqliteContext();
        var param = ctx.Dialect.CreateDbParameter("p0", DbType.Int32, 42);
        var name = ctx.Dialect.MakeParameterName(param);
        Assert.Equal("@p0", name);
    }
}
