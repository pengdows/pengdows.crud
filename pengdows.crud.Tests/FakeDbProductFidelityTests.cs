using System;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DEC-009: fakeDb behaves like the provider it emulates. Confirmed against the real drivers
/// (2026-10-04): Microsoft.Data.Sqlite treats a second Open() as a no-op and reports Database
/// "main"; DuckDB.NET throws on a second Open() and reports its data source as Database; Npgsql and
/// SqlClient throw on a second Open() and report "" when no database is named.
/// </summary>
public class FakeDbProductFidelityTests
{
    private static fakeDbConnection Open(SupportedDatabase db, string cs)
    {
        var connection = (fakeDbConnection)new fakeDbFactory(db).CreateConnection();
        connection.ConnectionString = cs;
        connection.Open();
        return connection;
    }

    [Fact]
    public void Sqlite_SecondOpen_IsANoOp()
    {
        using var connection = Open(SupportedDatabase.Sqlite, "Data Source=:memory:;EmulatedProduct=Sqlite");

        connection.Open();

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Theory]
    [InlineData(SupportedDatabase.DuckDB, "Data Source=:memory:;EmulatedProduct=DuckDB")]
    [InlineData(SupportedDatabase.PostgreSql, "Host=x;EmulatedProduct=PostgreSql")]
    [InlineData(SupportedDatabase.SqlServer, "Server=x;EmulatedProduct=SqlServer")]
    public void OtherProviders_SecondOpen_Throws(SupportedDatabase db, string cs)
    {
        using var connection = Open(db, cs);

        Assert.Throws<InvalidOperationException>(() => connection.Open());
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite, "Data Source=app.db;EmulatedProduct=Sqlite", "main")]
    [InlineData(SupportedDatabase.DuckDB, "Data Source=:memory:;EmulatedProduct=DuckDB", ":memory:")]
    [InlineData(SupportedDatabase.PostgreSql, "Host=x;EmulatedProduct=PostgreSql", "")]
    [InlineData(SupportedDatabase.SqlServer, "Server=x;Initial Catalog=app;EmulatedProduct=SqlServer", "app")]
    public void Database_IsWhatTheProviderReports(SupportedDatabase db, string cs, string expected)
    {
        using var connection = Open(db, cs);

        Assert.Equal(expected, connection.Database);
    }
}
