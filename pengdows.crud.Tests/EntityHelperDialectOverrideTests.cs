using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class TableGatewayDialectOverrideTests
{
    [Fact]
    public void BuildCreate_WithPostgresOverride_UsesAtMarker()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<TestEntity>();

        using var baseCtx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer",
            new fakeDbFactory(SupportedDatabase.SqlServer), typeMap);
        using var overrideCtx = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql",
            new fakeDbFactory(SupportedDatabase.PostgreSql), typeMap);

        var helper = new TableGateway<TestEntity, int>(baseCtx, new StubAuditValueResolver("tester"));
        var entity = new TestEntity { Name = "foo" };

        var sc = helper.BuildCreate(entity, overrideCtx);
        var sql = sc.Query.ToString();

        // PostgreSQL uses '@' (ADO.NET standard); not the SqlServer base context
        Assert.Contains("@i0", sql);
        Assert.DoesNotContain("$i0", sql);
    }

    [Fact]
    public void BuildDelete_WithPostgresOverride_UsesAtMarker()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<TestEntity>();

        using var baseCtx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer",
            new fakeDbFactory(SupportedDatabase.SqlServer), typeMap);
        using var overrideCtx = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql",
            new fakeDbFactory(SupportedDatabase.PostgreSql), typeMap);

        var helper = new TableGateway<TestEntity, int>(baseCtx, new StubAuditValueResolver("tester"));
        var sc = helper.BuildDelete(42, overrideCtx);
        var sql = sc.Query.ToString();

        // BuildDelete uses a generated parameter name; assert '@' marker is used
        Assert.Contains("@", sql);
        Assert.DoesNotContain("$", sql);
    }

    [Fact]
    public void BuildRetrieve_WithSqliteOverride_UsesAtMarkerInWhere()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<TestEntity>();

        using var baseCtx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer",
            new fakeDbFactory(SupportedDatabase.SqlServer), typeMap);
        using var overrideCtx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite), typeMap);

        var helper = new TableGateway<TestEntity, int>(baseCtx, new StubAuditValueResolver("tester"));
        var sc = helper.BuildRetrieve(new[] { 1, 2, 3 }, overrideCtx);
        var sql = sc.Query.ToString();

        // The IN (...) list should use '@' parameter markers under SQLite
        Assert.Contains("IN (", sql);
        Assert.Contains("@w0", sql);
        Assert.Contains("@w1", sql);
    }
}