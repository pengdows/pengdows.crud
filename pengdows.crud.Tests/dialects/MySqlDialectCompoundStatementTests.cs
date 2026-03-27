using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;
using pengdows.crud.fakeDb;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Tests for MySQL/MariaDB generated key strategy.
/// When using MySqlConnector, GetGeneratedKeyPlan returns ReaderInsertedId:
/// execute INSERT as a reader and read LastInsertedId from the provider's OK packet,
/// avoiding multi-statement (which MySqlConnector deliberately does not support) and
/// the session-scope two-lease hazard of a separate SELECT LAST_INSERT_ID() connection.
///
/// When using Oracle MySql.Data, GetGeneratedKeyPlan returns CompoundStatement (unchanged),
/// because that provider does support Allow Multiple Statements.
/// </summary>
public class MySqlDialectCompoundStatementTests
{
    [Fact]
    public void MySql_OracleProvider_GetGeneratedKeyPlan_Returns_CompoundStatement()
    {
        // fakeDbFactory namespace does not contain "MySqlConnector" → _isMySqlConnector = false
        // Oracle MySql.Data path: still uses CompoundStatement (multi-statement supported)
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);

        Assert.Equal(GeneratedKeyPlan.CompoundStatement, dialect.GetGeneratedKeyPlan());
    }

    [Fact]
    public void MySqlConnector_GetGeneratedKeyPlan_Returns_ReaderInsertedId()
    {
        // MySqlConnector 2.4.0 does not support AllowMultipleStatements.
        // ReaderInsertedId reads LastInsertedId from the MySqlDataReader OK packet — no multi-statement.
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);

        Assert.Equal(GeneratedKeyPlan.ReaderInsertedId, dialect.GetGeneratedKeyPlan());
    }

    [Fact]
    public void MariaDb_OracleProvider_GetGeneratedKeyPlan_Returns_CompoundStatement()
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
    public void MySqlConnector_PrepareConnectionString_DoesNotInjectAllowMultipleStatements()
    {
        // MySqlConnector 2.4.0 does not have AllowMultipleStatements as a connection string option.
        // Injecting it produces a connection string that MySqlConnector rejects at open time
        // (MySqlConnectionStringBuilder.set_Item throws ArgumentException for unknown keys).
        // PrepareConnectionStringForDataSource must NOT inject this key for the MySqlConnector path.
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);

        var cs = "Server=db;Database=app;User Id=sa;Password=secret";
        var result = dialect.PrepareConnectionStringForDataSource(cs, readOnly: false);

        Assert.DoesNotContain("AllowMultipleStatements", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MySqlData_PrepareConnectionString_InjectsAllowMultipleStatements()
    {
        // Oracle MySql.Data DOES support Allow Multiple Statements — injection must stay for that path.
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: false);

        var cs = "Server=db;Database=app;User Id=sa;Password=secret";
        var result = dialect.PrepareConnectionStringForDataSource(cs, readOnly: false);

        Assert.Contains("Allow Multiple Statements", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("True", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MySqlConnector_GetLastInsertedIdFromCommand_ReturnsReflectedLastInsertedId()
    {
        // GetLastInsertedIdFromCommand must use reflection to read LastInsertedId from MySqlCommand.
        // Verified with a test double that exposes the property without a hard MySqlConnector reference.
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);

        var command = new FakeCommandWithLastInsertedId(42L);
        var result = dialect.GetLastInsertedIdFromCommand(command);

        Assert.NotNull(result);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void MySqlConnector_GetLastInsertedIdFromCommand_ReturnsNull_WhenPropertyAbsent()
    {
        // Dialects without LastInsertedId on the command should return null.
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);

        // Use a plain DbCommand subclass without LastInsertedId
        var command = new FakeCommandWithoutLastInsertedId();
        var result = dialect.GetLastInsertedIdFromCommand(command);

        Assert.Null(result);
    }

    [Fact]
    public void MySqlConnector_GetLastInsertedIdFromCommand_ReturnsNull_WhenCommandIsNull()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);

        var result = dialect.GetLastInsertedIdFromCommand(null);

        Assert.Null(result);
    }

    [Fact]
    public void NonMySql_GetLastInsertedIdFromCommand_BaseImpl_ReturnsNull()
    {
        // The base SqlDialect.GetLastInsertedIdFromCommand always returns null.
        // Dialects that don't override it (SQLite, PostgreSQL, etc.) return null,
        // prompting the ReaderInsertedId plan to fall back to PopulateGeneratedIdAsync.
        using var ctx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        var result = ctx.GetDialect().GetLastInsertedIdFromCommand(null);
        Assert.Null(result);
    }

    [Fact]
    public void GetLastInsertedIdFromCommand_DifferentCommandTypes_ResolvedIndependently()
    {
        // Guards against a naive single-slot non-type-keyed cache:
        // if the cache stores one PropertyInfo for the first command type seen,
        // calling with a second (different) command type would either return wrong data
        // or throw TargetException. Property resolution must be keyed per command type.
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);

        // Command WITH the property
        var cmdWith = new FakeCommandWithLastInsertedId(42L);
        var result1 = dialect.GetLastInsertedIdFromCommand(cmdWith);

        // Command WITHOUT the property — a single-slot cache populated from cmdWith
        // would attempt prop.GetValue(cmdWithout) and throw TargetException.
        var cmdWithout = new FakeCommandWithoutLastInsertedId();
        var result2 = dialect.GetLastInsertedIdFromCommand(cmdWithout);

        // Command WITH the property again — must still resolve correctly
        var cmdWith2 = new FakeCommandWithLastInsertedId(99L);
        var result3 = dialect.GetLastInsertedIdFromCommand(cmdWith2);

        Assert.Equal(42L, result1);
        Assert.Null(result2);
        Assert.Equal(99L, result3);
    }
}

/// <summary>
/// Test double: a <see cref="System.Data.Common.DbCommand"/> subclass that exposes
/// <c>LastInsertedId</c>, mimicking the shape of <c>MySqlCommand</c> without
/// requiring a compile-time reference to MySqlConnector.
/// </summary>
file sealed class FakeCommandWithLastInsertedId : System.Data.Common.DbCommand
{
    public long LastInsertedId { get; }

    public FakeCommandWithLastInsertedId(long lastInsertedId)
    {
        LastInsertedId = lastInsertedId;
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override System.Data.CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override System.Data.UpdateRowSource UpdatedRowSource { get; set; }
    protected override System.Data.Common.DbConnection? DbConnection { get; set; }
    protected override System.Data.Common.DbParameterCollection DbParameterCollection => throw new System.NotImplementedException();
    protected override System.Data.Common.DbTransaction? DbTransaction { get; set; }
    public override void Cancel() { }
    public override int ExecuteNonQuery() => throw new System.NotImplementedException();
    public override object? ExecuteScalar() => throw new System.NotImplementedException();
    public override void Prepare() { }
    protected override System.Data.Common.DbParameter CreateDbParameter() => throw new System.NotImplementedException();
    protected override System.Data.Common.DbDataReader ExecuteDbDataReader(System.Data.CommandBehavior behavior) => throw new System.NotImplementedException();
}

/// <summary>
/// Test double: a <see cref="System.Data.Common.DbCommand"/> without <c>LastInsertedId</c>.
/// </summary>
file sealed class FakeCommandWithoutLastInsertedId : System.Data.Common.DbCommand
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override System.Data.CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override System.Data.UpdateRowSource UpdatedRowSource { get; set; }
    protected override System.Data.Common.DbConnection? DbConnection { get; set; }
    protected override System.Data.Common.DbParameterCollection DbParameterCollection => throw new System.NotImplementedException();
    protected override System.Data.Common.DbTransaction? DbTransaction { get; set; }
    public override void Cancel() { }
    public override int ExecuteNonQuery() => throw new System.NotImplementedException();
    public override object? ExecuteScalar() => throw new System.NotImplementedException();
    public override void Prepare() { }
    protected override System.Data.Common.DbParameter CreateDbParameter() => throw new System.NotImplementedException();
    protected override System.Data.Common.DbDataReader ExecuteDbDataReader(System.Data.CommandBehavior behavior) => throw new System.NotImplementedException();
}

/// <summary>
/// Covers the base-class <c>GetCompoundInsertIdSuffix()</c> throw path.
/// Dialects that do not override this method (e.g. SQL Server, which uses
/// SessionScopedFunction instead of CompoundStatement) should throw
/// <see cref="NotSupportedException"/>.
/// </summary>
public class SqlDialectBaseCompoundInsertIdSuffixTests
{
    [Fact]
    public void SqlServer_GetCompoundInsertIdSuffix_ThrowsNotSupportedException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);

        Assert.Throws<NotSupportedException>(() => dialect.GetCompoundInsertIdSuffix());
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
