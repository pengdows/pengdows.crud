using System.Collections.Generic;
using System.Reflection;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextProductDetectionTests
{
    public static IEnumerable<object[]> Products()
    {
        foreach (var db in System.Enum.GetValues<SupportedDatabase>())
        {
            if (db == SupportedDatabase.Unknown)
            {
                continue;
            }

            yield return new object[] { db };
        }
    }

    [Theory]
    [MemberData(nameof(Products))]
    public void InferProduct_DetectsKnownProducts(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        using var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={product}";
        conn.Open();
        var table = conn.GetSchema();
        table.Rows[0]["DataSourceProductName"] = product switch
        {
            SupportedDatabase.MariaDb => "MariaDB",
            SupportedDatabase.PostgreSql => "PostgreSQL",
            SupportedDatabase.SqlServer => "Microsoft SQL Server",
            SupportedDatabase.MySql => "MySQL",
            SupportedDatabase.CockroachDb => "CockroachDB",
            SupportedDatabase.Oracle => "Oracle",
            SupportedDatabase.Sqlite => "SQLite",
            SupportedDatabase.Firebird => "Firebird",
            SupportedDatabase.DuckDB => "DuckDB",
            _ => string.Empty
        };
        var tracked = new TrackedConnection(conn);
        using var ctx = new DatabaseContext(conn.ConnectionString, factory);
        var method = typeof(DatabaseContext).GetMethod("InferProduct", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var result = (SupportedDatabase)method!.Invoke(ctx, new object[] { tracked })!;
        Assert.Equal(product, result);
    }

    [Fact]
    public void InferProduct_UnknownWhenUnmatched()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        using var conn = factory.CreateConnection();
        conn.ConnectionString = "Data Source=test;EmulatedProduct=Unknown";
        conn.Open();
        var tracked = new TrackedConnection(conn);
        using var ctx = new DatabaseContext(conn.ConnectionString, factory);
        var method = typeof(DatabaseContext).GetMethod("InferProduct", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var result = (SupportedDatabase)method!.Invoke(ctx, new object[] { tracked })!;
        Assert.Equal(SupportedDatabase.Unknown, result);
    }
}
