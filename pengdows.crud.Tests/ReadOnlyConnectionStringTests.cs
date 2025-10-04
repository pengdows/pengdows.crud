using System;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class ReadOnlyConnectionStringTests
{
    [Fact]
    public void SqlServer_ReadOnly_SetsApplicationIntent()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        var ctx = new Mock<IDatabaseContext>();
        ctx.SetupGet(c => c.ConnectionString).Returns("Data Source=test;");
        dialect.ApplyConnectionSettings(conn, ctx.Object, true);
        Assert.Contains("ApplicationIntent=ReadOnly", conn.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServer_ReadWrite_DoesNotSetApplicationIntent()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        var ctx = new Mock<IDatabaseContext>();
        ctx.SetupGet(c => c.ConnectionString).Returns("Data Source=test;");
        dialect.ApplyConnectionSettings(conn, ctx.Object, false);
        Assert.Equal("Data Source=test;", conn.ConnectionString);
    }

    [Fact]
    public void PostgreSql_ReadOnly_AddsOptions()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger<PostgreSqlDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        var ctx = new Mock<IDatabaseContext>();
        ctx.SetupGet(c => c.ConnectionString).Returns("Host=test;");
        dialect.ApplyConnectionSettings(conn, ctx.Object, true);
        Assert.Contains("Options='-c default_transaction_read_only=on'", conn.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSql_ReadWrite_DoesNotAddOptions()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger<PostgreSqlDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        var ctx = new Mock<IDatabaseContext>();
        ctx.SetupGet(c => c.ConnectionString).Returns("Host=test;");
        dialect.ApplyConnectionSettings(conn, ctx.Object, false);
        Assert.Equal("Host=test;", conn.ConnectionString);
    }

    [Fact]
    public void Sqlite_ReadOnly_SetsMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        var ctx = new Mock<IDatabaseContext>();
        ctx.SetupGet(c => c.ConnectionString).Returns("Data Source=test.db;");
        dialect.ApplyConnectionSettings(conn, ctx.Object, true);
        Assert.Contains("Mode=ReadOnly", conn.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sqlite_ReadWrite_DoesNotSetMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        var ctx = new Mock<IDatabaseContext>();
        ctx.SetupGet(c => c.ConnectionString).Returns("Data Source=test.db;");
        dialect.ApplyConnectionSettings(conn, ctx.Object, false);
        Assert.Equal("Data Source=test.db;", conn.ConnectionString);
    }

    [Fact]
    public void Sqlite_InMemory_ReadOnly_DoesNotSetMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        var ctx = new Mock<IDatabaseContext>();
        ctx.SetupGet(c => c.ConnectionString).Returns("Data Source=:memory:");
        dialect.ApplyConnectionSettings(conn, ctx.Object, true);
        Assert.Equal("Data Source=:memory:", conn.ConnectionString);
    }

    [Fact]
    public void DuckDb_ReadOnly_SetsAccessMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var dialect = new DuckDbDialect(factory, NullLogger<DuckDbDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        var ctx = new Mock<IDatabaseContext>();
        ctx.SetupGet(c => c.ConnectionString).Returns("Data Source=test;");
        dialect.ApplyConnectionSettings(conn, ctx.Object, true);
        Assert.Contains("access_mode=READ_ONLY", conn.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuckDb_ReadWrite_DoesNotSetAccessMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var dialect = new DuckDbDialect(factory, NullLogger<DuckDbDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        var ctx = new Mock<IDatabaseContext>();
        ctx.SetupGet(c => c.ConnectionString).Returns("Data Source=test;");
        dialect.ApplyConnectionSettings(conn, ctx.Object, false);
        Assert.Equal("Data Source=test;", conn.ConnectionString);
    }

    [Fact]
    public void DuckDb_InMemory_ReadOnly_DoesNotSetAccessMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var dialect = new DuckDbDialect(factory, NullLogger<DuckDbDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        var ctx = new Mock<IDatabaseContext>();
        ctx.SetupGet(c => c.ConnectionString).Returns("Data Source=:memory:");
        dialect.ApplyConnectionSettings(conn, ctx.Object, true);
        Assert.Equal("Data Source=:memory:", conn.ConnectionString);
    }
}
