using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class ContextParameterUsageTests
{
    private static TypeMapRegistry SetupRegistry()
    {
        var map = new TypeMapRegistry();
        map.Register<TestEntity>();
        return map;
    }

    private static TestEntity CreateEntity()
    {
        return new TestEntity
        {
            Id = 1,
            Name = "foo",
            version = 1,
            CreatedBy = "u",
            CreatedOn = DateTime.UtcNow,
            LastUpdatedBy = "u",
            LastUpdatedOn = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task BuildUpdateAsync_UsesProvidedContextSqlContainer()
    {
        var map = SetupRegistry();
        using var defaultCtx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite),
            map);
        var helper = new EntityHelper<TestEntity, int>(defaultCtx, new StubAuditValueResolver("u"));
        var entity = CreateEntity();

        using var otherCtx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=MySql",
            new fakeDbFactory(SupportedDatabase.MySql),
            map);
        var sc = await helper.BuildUpdateAsync(entity, false, otherCtx);
        Assert.Equal("\"", sc.QuotePrefix);
        Assert.NotEqual("`", sc.QuotePrefix);
    }

    [Fact]
    public async Task BuildUpdateAsync_DefaultContextWhenNull()
    {
        var map = SetupRegistry();
        using var defaultCtx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite),
            map);
        var helper = new EntityHelper<TestEntity, int>(defaultCtx, new StubAuditValueResolver("u"));
        var entity = CreateEntity();

        var sc = await helper.BuildUpdateAsync(entity, false);
        Assert.Equal("\"", sc.QuotePrefix);
        Assert.NotEqual("`", sc.QuotePrefix);
    }
}