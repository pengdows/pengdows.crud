using System.Collections.Generic;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DialectDriftTests
{
    private static (DatabaseContext ctx, EntityHelper<TestEntity, int> helper) CreateHelper(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={db}", factory);
        var helper = new EntityHelper<TestEntity, int>(ctx);
        return (ctx, helper);
    }

    [Fact]
    public void BuildRetrieve_UsesEffectiveContext_MarkersDiffer()
    {
        // Arrange
        var (pgCtx, helper) = CreateHelper(SupportedDatabase.PostgreSql);
        var (sqliteCtx, _) = CreateHelper(SupportedDatabase.Sqlite);

        // Act
        var scPg = helper.BuildRetrieve(new[] { 1 }, context: pgCtx);
        var sqlPg = scPg.Query.ToString();

        var scSqlite = helper.BuildRetrieve(new[] { 2 }, context: sqliteCtx);
        var sqlSqlite = scSqlite.Query.ToString();

        // Assert
        Assert.Contains(":w0", sqlPg);       // PostgreSQL uses ':' marker
        Assert.Contains("@w0", sqlSqlite);    // SQLite uses '@' marker
    }

    [Fact]
    public void BuildRetrieve_Cache_DoesNotLeakAcrossDialects()
    {
        // Arrange
        var (sqliteCtx, helper) = CreateHelper(SupportedDatabase.Sqlite);
        var (pgCtx, _) = CreateHelper(SupportedDatabase.PostgreSql);

        // Prime cache with SQLite
        var sc1 = helper.BuildRetrieve(new[] { 42 }, context: sqliteCtx);
        var sql1 = sc1.Query.ToString();
        Assert.Contains("@w0", sql1);

        // Now render with PostgreSQL and ensure ':' is used (not '@')
        var sc2 = helper.BuildRetrieve(new[] { 43 }, context: pgCtx);
        var sql2 = sc2.Query.ToString();
        Assert.Contains(":w0", sql2);
        Assert.DoesNotContain("@w0", sql2);

        // And back to SQLite to ensure no regression
        var sc3 = helper.BuildRetrieve(new[] { 44 }, context: sqliteCtx);
        var sql3 = sc3.Query.ToString();
        Assert.Contains("@w0", sql3);
        Assert.DoesNotContain(":w0", sql3);
    }
}
