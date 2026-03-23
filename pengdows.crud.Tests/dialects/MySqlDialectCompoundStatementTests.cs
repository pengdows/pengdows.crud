using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Tests for MySQL/MariaDB compound INSERT+SELECT strategy.
/// GetGeneratedKeyPlan must return CompoundStatement so that CreateAsync
/// executes INSERT and SELECT LAST_INSERT_ID() in a single round-trip on
/// the same connection, avoiding the session-scope correctness hazard of
/// retrieving the generated key on a separate connection pool lease.
/// </summary>
public class MySqlDialectCompoundStatementTests
{
    [Fact]
    public void MySql_GetGeneratedKeyPlan_Returns_CompoundStatement()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);

        Assert.Equal(GeneratedKeyPlan.CompoundStatement, dialect.GetGeneratedKeyPlan());
    }

    [Fact]
    public void MariaDb_GetGeneratedKeyPlan_Returns_CompoundStatement()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var dialect = new MariaDbDialect(factory, NullLogger<MariaDbDialect>.Instance);

        Assert.Equal(GeneratedKeyPlan.CompoundStatement, dialect.GetGeneratedKeyPlan());
    }

    [Fact]
    public void MySql_GetCompoundInsertIdSuffix_Returns_LastInsertId()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);

        Assert.Equal("; SELECT LAST_INSERT_ID()", dialect.GetCompoundInsertIdSuffix());
    }

    [Fact]
    public void MariaDb_GetCompoundInsertIdSuffix_Returns_LastInsertId()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var dialect = new MariaDbDialect(factory, NullLogger<MariaDbDialect>.Instance);

        Assert.Equal("; SELECT LAST_INSERT_ID()", dialect.GetCompoundInsertIdSuffix());
    }

    [Fact]
    public void MySqlConnector_PrepareConnectionString_InjectsAllowMultipleStatements()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);

        var cs = "Server=db;Database=app;User Id=sa;Password=secret";
        var result = dialect.PrepareConnectionStringForDataSource(cs, readOnly: false);

        Assert.Contains("AllowMultipleStatements", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("True", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MySqlData_PrepareConnectionString_InjectsAllowMultipleStatements()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: false);

        var cs = "Server=db;Database=app;User Id=sa;Password=secret";
        var result = dialect.PrepareConnectionStringForDataSource(cs, readOnly: false);

        Assert.Contains("Allow Multiple Statements", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("True", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MySqlConnector_PrepareConnectionString_DoesNotDuplicate_WhenAlreadySet()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);

        var cs = "Server=db;Database=app;AllowMultipleStatements=true";
        var result = dialect.PrepareConnectionStringForDataSource(cs, readOnly: false);

        // Count occurrences — should appear exactly once
        var lower = result.ToLowerInvariant();
        var first = lower.IndexOf("allowmultiplestatements", StringComparison.Ordinal);
        var second = lower.IndexOf("allowmultiplestatements", first + 1, StringComparison.Ordinal);
        Assert.Equal(-1, second);
    }
}

/// <summary>
/// Tests for SQLite compound INSERT+SELECT strategy for versions before 3.35
/// (which lack RETURNING clause support).
/// </summary>
public class SqliteDialectCompoundStatementTests
{
    [Fact]
    public void Sqlite_OldVersion_GetGeneratedKeyPlan_Returns_CompoundStatement()
    {
        // Uninitialized dialect has no version → SupportsInsertReturning is false → CompoundStatement
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);

        Assert.Equal(GeneratedKeyPlan.CompoundStatement, dialect.GetGeneratedKeyPlan());
    }

    [Fact]
    public void Sqlite_GetCompoundInsertIdSuffix_Returns_LastInsertRowid()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);

        Assert.Equal("; SELECT last_insert_rowid()", dialect.GetCompoundInsertIdSuffix());
    }
}
