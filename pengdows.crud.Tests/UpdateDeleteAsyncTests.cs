#region
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
#endregion

namespace pengdows.crud.Tests;

public class UpdateDeleteAsyncTests : SqlLiteContextTestBase
{
    private readonly EntityHelper<TestEntity, int> helper;

    public UpdateDeleteAsyncTests()
    {
        TypeMap.Register<TestEntity>();
        helper = new EntityHelper<TestEntity, int>(Context, AuditValueResolver);
        BuildTestTable();
    }

    [Fact]
    public async Task UpdateAsync_WhenChanged_ReturnsOne()
    {
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(e, Context);
        var loaded = await helper.RetrieveOneAsync(e);
        Assert.NotNull(loaded);
        loaded!.Name = Guid.NewGuid().ToString();
        var count = await helper.UpdateAsync(loaded);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task UpdateAsync_AuditOnly_ReturnsOne()
    {
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(e, Context);
        var loaded = await helper.RetrieveOneAsync(e);
        Assert.NotNull(loaded);
        var originalUpdated = loaded!.LastUpdatedOn;
        var count = await helper.UpdateAsync(loaded);
        Assert.Equal(1, count);
        Assert.NotEqual(originalUpdated, loaded.LastUpdatedOn);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRow()
    {
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(e, Context);
        var loaded = await helper.RetrieveOneAsync(e);
        Assert.NotNull(loaded);
        var affected = await helper.DeleteAsync(loaded!.Id);
        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsRows()
    {
        await BuildTestTable();
        var e1 = new TestEntity { Name = Guid.NewGuid().ToString() };
        var e2 = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(e1, Context);
        await helper.CreateAsync(e2, Context);

        var ids = new List<int> { e1.Id, e2.Id };
        var result = await helper.RetrieveAsync(ids);
        Assert.Equal(ids.Count, result.Count);
    }

    [Fact]
    public async Task DeleteAsync_List_RemovesRows()
    {
        await BuildTestTable();
        var e1 = new TestEntity { Name = Guid.NewGuid().ToString() };
        var e2 = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(e1, Context);
        await helper.CreateAsync(e2, Context);

        var ids = new List<int> { e1.Id, e2.Id };
        var affected = await helper.DeleteAsync(ids);
        Assert.Equal(ids.Count, affected);
    }

    [Fact]
    public async Task RetrieveOneAsync_ById_ReturnsRow()
    {
        await BuildTestTable();
        var entity = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(entity, Context);

        var result = await helper.RetrieveOneAsync(entity.Id);

        Assert.NotNull(result);
        Assert.Equal(entity.Id, result!.Id);
    }

    [Fact]
    public async Task RetrieveOneAsync_ByEntity_ReturnsRow()
    {
        await BuildTestTable();
        var entity = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(entity, Context);

        var result = await helper.RetrieveOneAsync(entity);

        Assert.NotNull(result);
        Assert.Equal(entity.Id, result!.Id);
    }

    [Fact]
    public async Task BuildUpdateAsync_AuditOnly_IncludesAuditColumns()
    {
        await BuildTestTable();
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(e, Context);
        var loaded = await helper.RetrieveOneAsync(e);
        Assert.NotNull(loaded);
        var sc = await helper.BuildUpdateAsync(loaded!, true);
        var sql = sc.Query.ToString();
        Assert.Contains(Context.WrapObjectName("LastUpdatedOn"), sql);
    }

    private async Task BuildTestTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}Test{1} ({0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
{0}Name{1} TEXT UNIQUE NOT NULL,
    {0}CreatedBy{1} TEXT NOT NULL,
    {0}CreatedOn{1} TIMESTAMP NOT NULL,
    {0}LastUpdatedBy{1} TEXT NOT NULL,
    {0}LastUpdatedOn{1} TIMESTAMP NULL,
{0}Version{1} INTEGER NOT NULL DEFAULT 0)", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }
}
