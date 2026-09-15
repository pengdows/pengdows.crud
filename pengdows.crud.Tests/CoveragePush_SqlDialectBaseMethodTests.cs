using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Targeted tests for uncovered base SqlDialect virtual methods:
/// - BuildBatchInsertSql guard clauses (lines 268, 273, 278): empty tableName, null/empty columns, rowCount <= 0
/// - BuildBatchInsertSql delegation overload (line 257-258): no getValue arg
/// - BuildBatchUpdateSql NotSupportedException (line 250): dialect without batch update support
/// - WrapSimpleName("") → returns string.Empty (line 511)
/// - WrapObjectName("   ") → returns string.Empty (line 543)
/// - IsReadCommittedSnapshotOn base returns false (line 1395)
/// - IsUniqueViolation base returns false (line 1400)
/// </summary>
public class CoveragePush_SqlDialectBaseMethodTests
{
    // SQLite uses base SqlDialect implementation for BuildBatchInsertSql and BuildBatchUpdateSql.
    private static IDatabaseContext CreateSqliteContext()
    {
        return new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));
    }

    // =========================================================================
    // BuildBatchInsertSql guard clauses (lines 268, 273, 278)
    // =========================================================================

    [Fact]
    public void BuildBatchInsertSql_EmptyTableName_Throws()
    {
        // Line 268: throw new ArgumentException("Table name cannot be null or empty.")
        using var ctx = CreateSqliteContext();
        using var query = new SqlQueryBuilder();
        Assert.Throws<ArgumentException>(() =>
            ctx.GetDialect().BuildBatchInsertSql("", new[] { "col1" }, 1, query));
    }

    [Fact]
    public void BuildBatchInsertSql_NullColumnNames_Throws()
    {
        // Line 273: throw new ArgumentException("Column names cannot be null or empty.")
        using var ctx = CreateSqliteContext();
        using var query = new SqlQueryBuilder();
        Assert.Throws<ArgumentException>(() =>
            ctx.GetDialect().BuildBatchInsertSql("my_table", null!, 1, query));
    }

    [Fact]
    public void BuildBatchInsertSql_EmptyColumnList_Throws()
    {
        // Line 273: same guard for empty list
        using var ctx = CreateSqliteContext();
        using var query = new SqlQueryBuilder();
        Assert.Throws<ArgumentException>(() =>
            ctx.GetDialect().BuildBatchInsertSql("my_table", Array.Empty<string>(), 1, query));
    }

    [Fact]
    public void BuildBatchInsertSql_ZeroRowCount_Throws()
    {
        // Line 278: throw new ArgumentException("Row count must be greater than zero.")
        using var ctx = CreateSqliteContext();
        using var query = new SqlQueryBuilder();
        Assert.Throws<ArgumentException>(() =>
            ctx.GetDialect().BuildBatchInsertSql("my_table", new[] { "col1" }, 0, query));
    }

    // =========================================================================
    // BuildBatchInsertSql delegation overload (lines 257-258)
    // =========================================================================

    [Fact]
    public void BuildBatchInsertSql_WithoutGetValue_DelegatesToGetValueOverload()
    {
        // Lines 257-258: the no-getValue overload calls BuildBatchInsertSql(..., null).
        // Use Oracle (which overrides and delegates to base) or SQLite (which uses base directly).
        // Use a dialect that successfully builds batch insert SQL.
        using var ctx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=PostgreSql",
            new fakeDbFactory(SupportedDatabase.PostgreSql));
        using var query = new SqlQueryBuilder();
        // PostgreSQL uses base BuildBatchInsertSql — calling the no-getValue overload
        // delegates to the getValue=null overload (lines 257-258).
        ctx.GetDialect().BuildBatchInsertSql("\"t\"", new[] { "\"c\"" }, 1, query);
        Assert.Contains("INSERT INTO", query.ToString());
    }

    // =========================================================================
    // BuildBatchUpdateSql NotSupportedException (line 250)
    // =========================================================================

    [Fact]
    public void BuildBatchUpdateSql_OnUnsupportedDialect_ThrowsNotSupported()
    {
        // Line 250: throw new NotSupportedException("... does not support optimized batch updates.")
        // SQLite does not override BuildBatchUpdateSql → uses base which throws.
        using var ctx = CreateSqliteContext();
        using var query = new SqlQueryBuilder();
        Assert.Throws<NotSupportedException>(() =>
            ctx.GetDialect().BuildBatchUpdateSql("t", new[] { "col" }, new[] { "id" }, 1, query, null));
    }

    // =========================================================================
    // WrapSimpleName("") → string.Empty (line 511)
    // =========================================================================

    [Fact]
    public void WrapSimpleName_EmptyString_ReturnsEmpty()
    {
        // Line 511: if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        using var ctx = CreateSqliteContext();
        var result = ctx.GetDialect().WrapSimpleName(string.Empty);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void WrapSimpleName_WhitespaceString_ReturnsEmpty()
    {
        // Same line 511 guard with whitespace
        using var ctx = CreateSqliteContext();
        var result = ctx.GetDialect().WrapSimpleName("   ");
        Assert.Equal(string.Empty, result);
    }

    // =========================================================================
    // WrapObjectName("   ") → string.Empty (line 543)
    // =========================================================================

    [Fact]
    public void WrapObjectName_WhitespaceOnly_ReturnsEmpty()
    {
        // Line 543: trimmed.Length == 0 → return string.Empty
        using var ctx = CreateSqliteContext();
        var result = ctx.GetDialect().WrapObjectName("   ");
        Assert.Equal(string.Empty, result);
    }

    // =========================================================================
    // IsReadCommittedSnapshotOn base returns false (line 1395)
    // =========================================================================

    [Fact]
    public void IsReadCommittedSnapshotOn_BaseDialect_ReturnsFalse()
    {
        // Line 1395: base virtual returns false for dialects that don't override it.
        // SQLite's dialect inherits from base (no override) → should return false.
        using var ctx = CreateSqliteContext();
        var conn = ctx.GetConnection(ExecutionType.Read);
        try
        {
            var result = ctx.GetDialect().IsReadCommittedSnapshotOn(conn);
            Assert.False(result);
        }
        finally
        {
            ctx.CloseAndDisposeConnection(conn);
        }
    }

    // =========================================================================
    // IsUniqueViolation base returns false (line 1400)
    // =========================================================================

    [Fact]
    public void IsUniqueViolation_BaseDialect_ReturnsFalse()
    {
        // Line 1400: base virtual returns false for dialects that don't override it.
        // Use DuckDB — doesn't override IsUniqueViolation → base returns false.
        using var ctx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=DuckDB",
            new fakeDbFactory(SupportedDatabase.DuckDB));
        var ex = new StubDbException();
        var result = ctx.GetDialect().IsUniqueViolation(ex);
        Assert.False(result);
    }

    private sealed class StubDbException : DbException
    {
        public StubDbException() : base("stub") { }
    }
}
