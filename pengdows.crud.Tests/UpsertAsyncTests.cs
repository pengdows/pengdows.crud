#region
using System;
using System.Threading.Tasks;
using Xunit;
#endregion

namespace pengdows.crud.Tests;

public class UpsertAsyncTests : SqlLiteContextTestBase, IAsyncLifetime
{
    private readonly EntityHelper<TestEntity, int> helper;

    public UpsertAsyncTests()
    {
        TypeMap.Register<TestEntity>();
        helper = new EntityHelper<TestEntity, int>(Context, AuditValueResolver);
    }

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await BuildTestTable();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
    }

    [Fact]
    public async Task UpsertAsync_Inserts_WhenIdDefault()
    {
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        var affected = await helper.UpsertAsync(e);
        Assert.Equal(1, affected);
        var loaded = await helper.RetrieveOneAsync(e);
        Assert.NotNull(loaded);
        Assert.Equal(e.Name, loaded!.Name);
    }

    [Fact]
    public async Task UpsertAsync_Updates_WhenIdSet()
    {
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(e, Context);
        var loaded = await helper.RetrieveOneAsync(e);
        Assert.NotNull(loaded);
        var originalUpdated = loaded!.LastUpdatedOn;

        var affected = await helper.UpsertAsync(loaded);
        Assert.Equal(1, affected);
        var reloaded = await helper.RetrieveOneAsync(loaded.Id);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.LastUpdatedOn >= originalUpdated);
    }

    private async Task BuildTestTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}Test{1} ({0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
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
