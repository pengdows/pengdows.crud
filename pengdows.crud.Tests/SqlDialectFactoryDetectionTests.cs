using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectFactoryDetectionTests
{
    [Fact]
    public void CreateDialect_DetectsFromConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        using var conn = factory.CreateConnection();
        conn.ConnectionString = "EmulatedProduct=PostgreSql";
        conn.Open();
        var tracked = new TrackedConnection(conn);
        var dialect = SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);
        Assert.Equal(SupportedDatabase.PostgreSql, dialect.DatabaseType);
    }

    [Fact]
    public void CreateDialect_DetectsDuckDBFromConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        using var conn = factory.CreateConnection();
        conn.ConnectionString = "EmulatedProduct=DuckDB";
        conn.Open();
        var tracked = new TrackedConnection(conn);
        var dialect = SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);
        Assert.Equal(SupportedDatabase.DuckDB, dialect.DatabaseType);
    }

    [Fact]
    public void CreateDialect_UnknownProduct_ReturnsSql92Dialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        using var conn = factory.CreateConnection();
        conn.ConnectionString = "EmulatedProduct=Unknown";
        conn.Open();
        var tracked = new TrackedConnection(conn);
        var dialect = SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);
        Assert.IsType<Sql92Dialect>(dialect);
        Assert.Equal(SupportedDatabase.Unknown, dialect.DatabaseType);
    }
}
