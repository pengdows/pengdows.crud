using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Xunit;

namespace pengdows.crud.Tests;

public class ContextIdentityTests : SqlLiteContextTestBase
{
    private readonly EntityHelper<TestEntity, int> _helper;

    public ContextIdentityTests()
    {
        TypeMap.Register<TestEntity>();
        _helper = new EntityHelper<TestEntity, int>(Context);
    }

    [Fact]
    public void ContextIdentity_Interface_IsInternal()
    {
        Assert.False(typeof(IContextIdentity).IsPublic);
        Assert.True(typeof(IContextIdentity).IsNotPublic);
    }

    [Fact]
    public void BuildBaseRetrieve_WithTransactionFromSameRoot_Succeeds()
    {
        using var tx = Context.BeginTransaction();
        var sc = _helper.BuildBaseRetrieve("a", tx);
        Assert.NotNull(sc);
    }

    [Fact]
    public void BuildBaseRetrieve_WithDifferentContext_Throws()
    {
        var otherMap = new TypeMapRegistry();
        otherMap.Register<TestEntity>();
        using var other = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance, otherMap);
        Assert.Throws<InvalidOperationException>(() => _helper.BuildBaseRetrieve("a", other));
    }

    [Fact]
    public void BuildRetrieve_ByObject_WithAlias_UsesAlias()
    {
        var list = new List<TestEntity> { new() { Id = 1, Name = "foo" } };
        var sc = _helper.BuildRetrieve(list, "x");
        var sql = sc.Query.ToString();
        Assert.Contains("x.\"Name\"", sql);
    }

    [Fact]
    public void BuildRetrieve_WithoutAlias_DoesNotUseAlias()
    {
        var list = (new List<TestEntity> { new() { Id = 1, Name = "foo" } }).AsReadOnly();
        var sc = _helper.BuildRetrieve(list);
        var sql = sc.Query.ToString();
        Assert.DoesNotContain("x.\"Name\"", sql);
    }

    [Fact]
    public void BuildUpsert_WithTransactionFromSameRoot_Succeeds()
    {
        using var tx = Context.BeginTransaction();
        var entity = new TestEntity { Id = 1, Name = "foo" };
        var sc = _helper.BuildUpsert(entity, tx);
        Assert.NotNull(sc);
    }

    [Fact]
    public void BuildUpsert_WithDifferentContext_Throws()
    {
        var otherMap = new TypeMapRegistry();
        otherMap.Register<TestEntity>();
        using var other = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance, otherMap);
        var entity = new TestEntity { Id = 1, Name = "foo" };
        Assert.Throws<InvalidOperationException>(() => _helper.BuildUpsert(entity, other));
    }
}