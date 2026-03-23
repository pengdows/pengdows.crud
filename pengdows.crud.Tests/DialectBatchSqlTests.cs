#region

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;
using Xunit.Abstractions;

#endregion

namespace pengdows.crud.Tests;

public class DialectBatchSqlTests
{
    private readonly ISqlQueryBuilder _query;
    private readonly List<string> _columns;
    private readonly List<string> _keyColumns;
    private readonly ITestOutputHelper _output;

    public DialectBatchSqlTests(ITestOutputHelper output)
    {
        _query = new SqlQueryBuilder();
        _columns = new List<string> { "\"name\"", "\"age\"" };
        _keyColumns = new List<string> { "\"id\"" };
        _output = output;
    }

    private Func<int, int, object?> GetStandardValues() => (row, col) => "val";

    [Fact]
    public void OracleDialect_BuildBatchInsertSql_UsesInsertAll()
    {
        // Arrange
        var dialect = new OracleDialect(new fakeDbFactory(SupportedDatabase.Oracle), NullLogger.Instance);

        // Act
        dialect.BuildBatchInsertSql("\"my_table\"", _columns, 2, _query, GetStandardValues());
        var sql = NormalizeSql(_query.ToString());
        _output.WriteLine(sql);

        // Assert
        Assert.Contains("INSERT ALL", sql);
        Assert.Contains("INTO \"my_table\" (\"name\", \"age\") VALUES (:b0, :b1)", sql);
        Assert.Contains("INTO \"my_table\" (\"name\", \"age\") VALUES (:b2, :b3)", sql);
        Assert.Contains("SELECT 1 FROM DUAL", sql);
    }

    [Fact]
    public void FirebirdDialect_BuildBatchInsertSql_UsesExecuteBlock()
    {
        // Arrange
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird), NullLogger.Instance);

        // Act
        dialect.BuildBatchInsertSql("\"my_table\"", _columns, 2, _query, GetStandardValues());
        var sql = NormalizeSql(_query.ToString());
        _output.WriteLine(sql);

        // Assert
        Assert.Contains("EXECUTE BLOCK AS BEGIN", sql);
        Assert.Contains("INSERT INTO \"my_table\" (\"name\", \"age\") VALUES (@b0, @b1);", sql);
        Assert.Contains("INSERT INTO \"my_table\" (\"name\", \"age\") VALUES (@b2, @b3);", sql);
        Assert.Contains("END", sql);
    }

    [Fact]
    public void SnowflakeDialect_BuildBatchInsertSql_UsesMultiRowValues()
    {
        // Arrange
        var dialect = new SnowflakeDialect(new fakeDbFactory(SupportedDatabase.Snowflake), NullLogger.Instance);

        // Act
        dialect.BuildBatchInsertSql("\"my_table\"", _columns, 2, _query, GetStandardValues());
        var sql = NormalizeSql(_query.ToString());
        _output.WriteLine(sql);

        // Assert
        Assert.Contains("INSERT INTO \"my_table\" (\"name\", \"age\") VALUES (:b0, :b1), (:b2, :b3)", sql);
    }

    [Fact]
    public void Dialect_BuildBatchInsertSql_InlinesNulls()
    {
        // Arrange
        // Note: PostgreSqlDialect uses @ marker (ADO.NET standard)
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);
        Func<int, int, object?> getValue = (row, col) => (row == 0 && col == 1) ? null : "val";

        // Act
        dialect.BuildBatchInsertSql("\"t\"", _columns, 2, _query, getValue);
        var sql = NormalizeSql(_query.ToString());
        _output.WriteLine(sql);

        // Assert
        // Row 0: ("val", NULL) -> @b0 for col 0, NULL for col 1
        // Row 1: ("val", "val") -> @b1 for col 0, @b2 for col 1
        Assert.Contains("VALUES (@b0, NULL), (@b1, @b2)", sql);
    }

    // =========================================================================
    // BuildBatchInsertSql — per-dialect coverage (standard multi-row VALUES)
    // =========================================================================

    [Fact]
    public void SqlServerDialect_BuildBatchInsertSql_UsesMultiRowValues()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);

        dialect.BuildBatchInsertSql("\"my_table\"", _columns, 2, _query, GetStandardValues());
        var sql = NormalizeSql(_query.ToString());
        _output.WriteLine(sql);

        Assert.Contains("INSERT INTO \"my_table\" (\"name\", \"age\") VALUES", sql);
        Assert.Contains("(@b0, @b1)", sql);
        Assert.Contains("(@b2, @b3)", sql);
    }

    [Fact]
    public void PostgreSqlDialect_BuildBatchInsertSql_UsesMultiRowValues()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);

        dialect.BuildBatchInsertSql("\"my_table\"", _columns, 2, _query, GetStandardValues());
        var sql = NormalizeSql(_query.ToString());
        _output.WriteLine(sql);

        Assert.Contains("INSERT INTO \"my_table\" (\"name\", \"age\") VALUES", sql);
        Assert.Contains("(@b0, @b1)", sql);
        Assert.Contains("(@b2, @b3)", sql);
    }

    [Fact]
    public void MySqlDialect_BuildBatchInsertSql_UsesMultiRowValues()
    {
        var dialect = new MySqlDialect(new fakeDbFactory(SupportedDatabase.MySql), NullLogger.Instance);

        dialect.BuildBatchInsertSql("\"my_table\"", _columns, 2, _query, GetStandardValues());
        var sql = NormalizeSql(_query.ToString());
        _output.WriteLine(sql);

        Assert.Contains("INSERT INTO \"my_table\" (\"name\", \"age\") VALUES", sql);
        Assert.Contains("(@b0, @b1)", sql);
        Assert.Contains("(@b2, @b3)", sql);
    }

    [Fact]
    public void SqliteDialect_BuildBatchInsertSql_UsesMultiRowValues()
    {
        var dialect = new SqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger.Instance);

        dialect.BuildBatchInsertSql("\"my_table\"", _columns, 2, _query, GetStandardValues());
        var sql = NormalizeSql(_query.ToString());
        _output.WriteLine(sql);

        Assert.Contains("INSERT INTO \"my_table\" (\"name\", \"age\") VALUES", sql);
        Assert.Contains("(@b0, @b1)", sql);
        Assert.Contains("(@b2, @b3)", sql);
    }

    [Fact]
    public void DuckDbDialect_BuildBatchInsertSql_UsesMultiRowValues()
    {
        var dialect = new DuckDbDialect(new fakeDbFactory(SupportedDatabase.DuckDB), NullLogger.Instance);

        dialect.BuildBatchInsertSql("\"my_table\"", _columns, 2, _query, GetStandardValues());
        var sql = NormalizeSql(_query.ToString());
        _output.WriteLine(sql);

        Assert.Contains("INSERT INTO \"my_table\" (\"name\", \"age\") VALUES", sql);
        Assert.Contains("($b0, $b1)", sql);
        Assert.Contains("($b2, $b3)", sql);
    }

    // =========================================================================
    // BuildBatchUpdateSql — per-dialect coverage
    // =========================================================================

    [Fact]
    public void PostgreSqlDialect_BuildBatchUpdateSql_UsesUpdateFromValues()
    {
        // PostgreSQL: UPDATE t AS t SET col=s.col FROM (VALUES ...) AS s(key,col...) WHERE t.key=s.key
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);
        var query = new SqlQueryBuilder();

        dialect.BuildBatchUpdateSql("\"my_table\"", _columns, _keyColumns, 1, query, GetStandardValues());
        var sql = NormalizeSql(query.ToString());
        _output.WriteLine(sql);

        Assert.Contains("UPDATE \"my_table\" AS t SET", sql);
        Assert.Contains("\"name\" = s.\"name\"", sql);
        Assert.Contains("FROM (VALUES (@b0, @b1, @b2))", sql);
        Assert.Contains("AS s(\"id\", \"name\", \"age\")", sql);
        Assert.Contains("WHERE t.\"id\" = s.\"id\"", sql);
    }

    [Fact]
    public void SqlServerDialect_BuildBatchUpdateSql_UsesMerge()
    {
        // SQL Server: MERGE INTO t AS t USING (VALUES ...) AS s(key,col...) ON t.key=s.key WHEN MATCHED THEN UPDATE SET col=s.col;
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);
        var query = new SqlQueryBuilder();

        dialect.BuildBatchUpdateSql("\"my_table\"", _columns, _keyColumns, 1, query, GetStandardValues());
        var sql = NormalizeSql(query.ToString());
        _output.WriteLine(sql);

        Assert.Contains("MERGE INTO \"my_table\" AS t USING (VALUES", sql);
        Assert.Contains("(@b0, @b1, @b2)", sql);
        Assert.Contains("AS s(\"id\", \"name\", \"age\")", sql);
        Assert.Contains("ON (t.\"id\" = s.\"id\")", sql);
        Assert.Contains("WHEN MATCHED THEN UPDATE SET", sql);
        Assert.Contains("\"name\" = s.\"name\"", sql);
        Assert.EndsWith(";", sql);
    }

    [Fact]
    public void SnowflakeDialect_BuildBatchUpdateSql_UsesUpdateFromValues_NoTargetAlias()
    {
        // Snowflake: UPDATE t SET col=s.col FROM (VALUES ...) AS s(key,col...) WHERE t.key=s.key
        // Difference from PostgreSQL: no "AS t" alias on target; WHERE uses tableName.key not t.key
        var dialect = new SnowflakeDialect(new fakeDbFactory(SupportedDatabase.Snowflake), NullLogger.Instance);
        var query = new SqlQueryBuilder();

        dialect.BuildBatchUpdateSql("\"my_table\"", _columns, _keyColumns, 1, query, GetStandardValues());
        var sql = NormalizeSql(query.ToString());
        _output.WriteLine(sql);

        Assert.Contains("UPDATE \"my_table\" SET", sql);
        Assert.DoesNotContain("UPDATE \"my_table\" AS t", sql);
        Assert.Contains("FROM (VALUES (:b0, :b1, :b2))", sql);
        Assert.Contains("AS s(\"id\", \"name\", \"age\")", sql);
        Assert.Contains("WHERE \"my_table\".\"id\" = s.\"id\"", sql);
    }

    [Fact]
    public void Dialect_BuildBatchUpdateSql_InlinesNulls()
    {
        // col 0 = id (key), col 1 = name (null → inline NULL), col 2 = age (non-null)
        // Expected VALUES: (@b0, NULL, @b1)  — paramIdx skips the NULL slot
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);
        var query = new SqlQueryBuilder();
        Func<int, int, object?> getValue = (row, col) => col == 1 ? (object?)null : "val";

        dialect.BuildBatchUpdateSql("\"my_table\"", _columns, _keyColumns, 1, query, getValue);
        var sql = NormalizeSql(query.ToString());
        _output.WriteLine(sql);

        Assert.Contains("(@b0, NULL, @b1)", sql);
    }

    [Fact]
    public void PostgreSqlDialect_BuildBatchUpdateSql_MultiRow_CorrectParamIndexing()
    {
        // Two rows: VALUES (@b0,@b1,@b2), (@b3,@b4,@b5) — paramIdx is sequential across all rows.
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);
        var query = new SqlQueryBuilder();

        dialect.BuildBatchUpdateSql("\"my_table\"", _columns, _keyColumns, 2, query, GetStandardValues());
        var sql = NormalizeSql(query.ToString());
        _output.WriteLine(sql);

        Assert.Contains("(@b0, @b1, @b2)", sql);
        Assert.Contains("(@b3, @b4, @b5)", sql);
    }

    [Fact]
    public void SqlServerDialect_BuildBatchUpdateSql_MultiRow_CorrectParamIndexing()
    {
        // Two rows: VALUES (@b0,@b1,@b2), (@b3,@b4,@b5)
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);
        var query = new SqlQueryBuilder();

        dialect.BuildBatchUpdateSql("\"my_table\"", _columns, _keyColumns, 2, query, GetStandardValues());
        var sql = NormalizeSql(query.ToString());
        _output.WriteLine(sql);

        Assert.Contains("(@b0, @b1, @b2), (@b3, @b4, @b5)", sql);
    }

    [Fact]
    public void Dialect_BuildBatchUpdateSql_ZeroRows_EmitsNothing()
    {
        // rowCount <= 0 should produce an empty query (early return guard).
        var pgDialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);
        var pgQuery = new SqlQueryBuilder();
        pgDialect.BuildBatchUpdateSql("\"my_table\"", _columns, _keyColumns, 0, pgQuery, GetStandardValues());
        Assert.Equal(string.Empty, pgQuery.ToString());

        var ssDialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);
        var ssQuery = new SqlQueryBuilder();
        ssDialect.BuildBatchUpdateSql("\"my_table\"", _columns, _keyColumns, 0, ssQuery, GetStandardValues());
        Assert.Equal(string.Empty, ssQuery.ToString());

        var sfDialect = new SnowflakeDialect(new fakeDbFactory(SupportedDatabase.Snowflake), NullLogger.Instance);
        var sfQuery = new SqlQueryBuilder();
        sfDialect.BuildBatchUpdateSql("\"my_table\"", _columns, _keyColumns, 0, sfQuery, GetStandardValues());
        Assert.Equal(string.Empty, sfQuery.ToString());
    }

    private static string NormalizeSql(string sql)
    {
        return Regex.Replace(sql, @"\s+", " ").Trim();
    }
}
