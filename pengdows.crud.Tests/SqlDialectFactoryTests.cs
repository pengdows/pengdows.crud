using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectFactoryTests
{
    [Fact]
    public void CreateDialectForType_SqlServer_ReturnsSqlServerDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.SqlServer,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<SqlServerDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_MySql_ReturnsMySqlDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.MySql,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<MySqlDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_MariaDb_ReturnsMariaDbDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.MariaDb,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<MariaDbDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_PostgreSql_ReturnsPostgreSqlDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.PostgreSql,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<PostgreSqlDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_Oracle_ReturnsOracleDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.Oracle,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<OracleDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_Sqlite_ReturnsSqliteDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.Sqlite,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<SqliteDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_Firebird_ReturnsFirebirdDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.Firebird,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<FirebirdDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_DuckDB_ReturnsDuckDbDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.DuckDB,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<DuckDbDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_Unknown_ReturnsSql92Dialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.Unknown,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<Sql92Dialect>(dialect);
    }
}