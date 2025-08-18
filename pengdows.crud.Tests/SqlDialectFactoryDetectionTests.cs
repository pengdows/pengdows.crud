using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectFactoryDetectionTests
{
    [Fact]
    public void CreateDialect_DetectsFromConnection()
    {
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        using var conn = factory.CreateConnection();
        conn.ConnectionString = "EmulatedProduct=PostgreSql";
        conn.Open();
        var tracked = new TrackedConnection(conn);
        var dialect = SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);
        Assert.Equal(SupportedDatabase.PostgreSql, dialect.DatabaseType);
    }

    [Fact]
    public void CreateDialect_UnknownProduct_Throws()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Unknown);
        using var conn = factory.CreateConnection();
        conn.ConnectionString = "EmulatedProduct=Unknown";
        conn.Open();
        var tracked = new TrackedConnection(conn);
        Assert.Throws<ArgumentException>(() =>
            SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance));
    }
}
