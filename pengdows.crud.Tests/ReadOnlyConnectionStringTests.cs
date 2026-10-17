using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        dialect.ApplyConnectionSettings(conn, ctx, true);
        Assert.Contains("ApplicationIntent=ReadOnly", conn.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServer_ReadWrite_DoesNotSetApplicationIntent()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        dialect.ApplyConnectionSettings(conn, ctx, false);
        Assert.DoesNotContain("ApplicationIntent", conn.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async System.Threading.Tasks.Task PostgreSql_ReadOnly_AddsOptions()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        await using var ctx = new DatabaseContext(new pengdows.crud.configuration.DatabaseContextConfiguration
        {
            ConnectionString = "Host=test;EmulatedProduct=PostgreSql",
            ReadWriteMode = ReadWriteMode.ReadOnly
        }, factory);

        await using var sc = ctx.CreateSqlContainer("SELECT 1");
        await sc.ExecuteScalarOrNullAsync<int>();

        Assert.Contains(factory.CreatedConnections, c => c.ConnectionString.Contains(
            "default_transaction_read_only=on", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PostgreSql_ReadWrite_DoesNotAddOptions()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger<PostgreSqlDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        using var ctx = new DatabaseContext("Host=test;EmulatedProduct=PostgreSql", factory);
        dialect.ApplyConnectionSettings(conn, ctx, false);
        // A read-write PostgreSQL connection sets it off explicitly; what must not happen is turning it on.
        Assert.DoesNotContain("default_transaction_read_only=on", conn.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sqlite_ReadOnly_SetsMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        using var ctx = new DatabaseContext("Data Source=test.db;EmulatedProduct=Sqlite", factory);
        dialect.ApplyConnectionSettings(conn, ctx, true);
        Assert.Contains("Mode=ReadOnly", conn.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sqlite_ReadWrite_DoesNotSetMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        using var ctx = new DatabaseContext("Data Source=test.db;EmulatedProduct=Sqlite", factory);
        dialect.ApplyConnectionSettings(conn, ctx, false);
        Assert.Equal(ctx.ConnectionString, conn.ConnectionString);
    }

    [Fact]
    public void Sqlite_InMemory_ReadOnly_DoesNotSetMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        using var ctx = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
        dialect.ApplyConnectionSettings(conn, ctx, true);
        Assert.Equal(ctx.ConnectionString, conn.ConnectionString);
    }

    [Fact]
    public void DuckDb_ReadOnly_SetsAccessMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var dialect = new DuckDbDialect(factory, NullLogger<DuckDbDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=DuckDB", factory);
        dialect.ApplyConnectionSettings(conn, ctx, true);
        Assert.Contains("access_mode=READ_ONLY", conn.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuckDb_ReadWrite_DoesNotSetAccessMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var dialect = new DuckDbDialect(factory, NullLogger<DuckDbDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=DuckDB", factory);
        dialect.ApplyConnectionSettings(conn, ctx, false);
        Assert.Equal(ctx.ConnectionString, conn.ConnectionString);
    }

    [Fact]
    public void DuckDb_InMemory_ReadOnly_DoesNotSetAccessMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var dialect = new DuckDbDialect(factory, NullLogger<DuckDbDialect>.Instance);
        using var conn = (fakeDbConnection)factory.CreateConnection();
        using var ctx = new DatabaseContext("Data Source=:memory:;EmulatedProduct=DuckDB", factory);
        dialect.ApplyConnectionSettings(conn, ctx, true);
        Assert.Equal(ctx.ConnectionString, conn.ConnectionString);
    }
}