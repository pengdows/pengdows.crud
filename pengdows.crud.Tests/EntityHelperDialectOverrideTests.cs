using Xunit;
using pengdows.crud.fakeDb;
using pengdows.crud.enums;

namespace pengdows.crud.Tests;

public class EntityHelperDialectOverrideTests
{
    [Fact]
    public void BuildCreate_WithPostgresOverride_UsesColonMarker()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<TestEntity>();

        using var baseCtx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", new fakeDbFactory(SupportedDatabase.SqlServer), typeMap);
        using var overrideCtx = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", new fakeDbFactory(SupportedDatabase.PostgreSql), typeMap);

        var helper = new EntityHelper<TestEntity, int>(baseCtx, new StubAuditValueResolver("tester"));
        var entity = new TestEntity { Name = "foo" };

        var sc = helper.BuildCreate(entity, overrideCtx);
        var sql = sc.Query.ToString();

        // Postgres uses ':' for named parameters
        Assert.Contains(":i0", sql);
        Assert.DoesNotContain("@i0", sql);
    }

    [Fact]
    public void BuildDelete_WithPostgresOverride_UsesColonMarker()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<TestEntity>();

        using var baseCtx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", new fakeDbFactory(SupportedDatabase.SqlServer), typeMap);
        using var overrideCtx = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", new fakeDbFactory(SupportedDatabase.PostgreSql), typeMap);

        var helper = new EntityHelper<TestEntity, int>(baseCtx, new StubAuditValueResolver("tester"));
        var sc = helper.BuildDelete(42, overrideCtx);
        var sql = sc.Query.ToString();

        // BuildDelete uses a generated parameter name, so assert marker prefix only
        Assert.Contains(":", sql);
        Assert.DoesNotContain("@", sql);
    }

    [Fact]
    public void BuildRetrieve_WithSqliteOverride_UsesAtMarkerInWhere()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<TestEntity>();

        using var baseCtx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", new fakeDbFactory(SupportedDatabase.SqlServer), typeMap);
        using var overrideCtx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite), typeMap);

        var helper = new EntityHelper<TestEntity, int>(baseCtx, new StubAuditValueResolver("tester"));
        var sc = helper.BuildRetrieve(new[] { 1, 2, 3 }, overrideCtx);
        var sql = sc.Query.ToString();

        // The IN (...) list should use '@' parameter markers under SQLite
        Assert.Contains("IN (", sql);
        Assert.Contains("@w0", sql);
        Assert.Contains("@w1", sql);
    }
}

