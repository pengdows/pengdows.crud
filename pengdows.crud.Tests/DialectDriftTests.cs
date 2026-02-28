using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class DialectDriftTests
{
    private static (DatabaseContext ctx, TableGateway<TestEntity, int> helper) CreateHelper(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={db}", factory);
        var helper = new TableGateway<TestEntity, int>(ctx);
        return (ctx, helper);
    }

    [Fact]
    public void BuildRetrieve_UsesEffectiveContext_MarkersDiffer()
    {
        // Arrange — DuckDB ('$') vs SQLite ('@') gives an observable distinction.
        // PostgreSQL also uses '@' after adopting the ADO.NET standard.
        var (duckCtx, helper) = CreateHelper(SupportedDatabase.DuckDB);
        var (sqliteCtx, _) = CreateHelper(SupportedDatabase.Sqlite);

        // Act
        var scDuck = helper.BuildRetrieve(new[] { 1 }, duckCtx);
        var sqlDuck = scDuck.Query.ToString();

        var scSqlite = helper.BuildRetrieve(new[] { 2 }, sqliteCtx);
        var sqlSqlite = scSqlite.Query.ToString();

        // Assert
        Assert.Contains("$p0", sqlDuck);   // DuckDB uses '$' marker
        Assert.Contains("@p0", sqlSqlite); // SQLite uses '@' marker
    }

    [Fact]
    public void BuildRetrieve_Cache_DoesNotLeakAcrossDialects()
    {
        // Arrange — DuckDB ('$') gives an observable distinction from SQLite ('@').
        var (sqliteCtx, helper) = CreateHelper(SupportedDatabase.Sqlite);
        var (duckCtx, _) = CreateHelper(SupportedDatabase.DuckDB);

        // Prime cache with SQLite
        var sc1 = helper.BuildRetrieve(new[] { 42 }, sqliteCtx);
        var sql1 = sc1.Query.ToString();
        Assert.Contains("@p0", sql1);

        // Now render with DuckDB and ensure '$' is used (not '@')
        var sc2 = helper.BuildRetrieve(new[] { 43 }, duckCtx);
        var sql2 = sc2.Query.ToString();
        Assert.Contains("$p0", sql2);
        Assert.DoesNotContain("@p0", sql2);

        // And back to SQLite to ensure no regression
        var sc3 = helper.BuildRetrieve(new[] { 44 }, sqliteCtx);
        var sql3 = sc3.Query.ToString();
        Assert.Contains("@p0", sql3);
        Assert.DoesNotContain("$p0", sql3);
    }
}