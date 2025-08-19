using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.dialects;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectFactoryTests
{
    [Fact]
    public void CreateDialectForType_SqlServer_ReturnsSqlServerDialect()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.SqlServer,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<SqlServerDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_MySql_ReturnsMySqlDialect()
    {
        var factory = new FakeDbFactory(SupportedDatabase.MySql);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.MySql,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<MySqlDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_PostgreSql_ReturnsPostgreSqlDialect()
    {
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.PostgreSql,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<PostgreSqlDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_Oracle_ReturnsOracleDialect()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Oracle);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.Oracle,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<OracleDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_Sqlite_ReturnsSqliteDialect()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.Sqlite,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<SqliteDialect>(dialect);
    }

    [Fact]
    public void CreateDialectForType_Firebird_ThrowsNotImplementedException()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Firebird);
        Assert.Throws<NotImplementedException>(() =>
            SqlDialectFactory.CreateDialectForType(
                SupportedDatabase.Firebird,
                factory,
                    NullLogger<SqlDialect>.Instance));
    }

    [Fact]
    public void CreateDialectForType_Unknown_ThrowsArgumentException()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Unknown);
        Assert.Throws<ArgumentException>(() =>
            SqlDialectFactory.CreateDialectForType(
                SupportedDatabase.Unknown,
                factory,
                NullLogger<SqlDialect>.Instance));
    }
}
