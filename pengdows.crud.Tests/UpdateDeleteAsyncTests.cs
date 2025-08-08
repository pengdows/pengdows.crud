#region
using System;
using System.Linq;
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
        helper = new EntityHelper<TestEntity, int>(Context);
        BuildTestTable();
    }

    [Fact]
    public async Task UpdateAsync_WhenChanged_ReturnsOne()
    {
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(e, Context);
        var loaded = (await helper.LoadListAsync(helper.BuildBaseRetrieve("a"))).First();
        loaded.Name = Guid.NewGuid().ToString();
        var count = await helper.UpdateAsync(loaded);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task UpdateAsync_WhenNoChange_ReturnsZero()
    {
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(e, Context);
        var loaded = (await helper.LoadListAsync(helper.BuildBaseRetrieve("a"))).First();
        var originalUpdated = loaded.LastUpdatedOn;
        var count = await helper.UpdateAsync(loaded);
        Assert.Equal(0, count);
        Assert.Equal(originalUpdated, loaded.LastUpdatedOn);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRow()
    {
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(e, Context);
        var loaded = (await helper.LoadListAsync(helper.BuildBaseRetrieve("a"))).First();
        var affected = await helper.DeleteAsync(loaded.Id);
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

        var ids = (await helper.LoadListAsync(helper.BuildBaseRetrieve("a"))).Select(x => x.Id).ToList();
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

        var ids = (await helper.LoadListAsync(helper.BuildBaseRetrieve("a"))).Select(x => x.Id).ToList();
        var affected = await helper.DeleteAsync(ids);
        Assert.Equal(ids.Count, affected);
    }

    [Fact]
    public async Task RetrieveOneAsync_ById_ReturnsRow()
    {
        await BuildTestTable();
        var entity = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(entity, Context);

        var loaded = (await helper.LoadListAsync(helper.BuildBaseRetrieve("a"))).First();
        var result = await helper.RetrieveOneAsync(loaded.Id);

        Assert.NotNull(result);
        Assert.Equal(loaded.Id, result!.Id);
    }

    private async Task BuildTestTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}Test{1} ({0}Id{1} INTEGER PRIMARY KEY,
{0}Name{1} TEXT UNIQUE NOT NULL,
    {0}CreatedBy{1} TEXT NOT NULL DEFAULT 'system',
    {0}CreatedOn{1} TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    {0}LastUpdatedBy{1} TEXT NOT NULL DEFAULT 'system',
    {0}LastUpdatedOn{1} TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
{0}Version{1} INTEGER NOT NULL DEFAULT 0)", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }
}
