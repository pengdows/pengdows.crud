#region

using System;
using System.Threading.Tasks;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class UpsertPortableTests : SqlLiteContextTestBase, IAsyncLifetime
{
    private readonly EntityHelper<TestEntity, int> _helper;

    public UpsertPortableTests()
    {
        TypeMap.Register<TestEntity>();
        _helper = new EntityHelper<TestEntity, int>(Context, AuditValueResolver);
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
    public async Task UpsertAsync_PortableInsertAndUpdate()
    {
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        var affected = await _helper.UpsertAsync(e, Context);
        Assert.Equal(1, affected);
        affected = await _helper.UpsertAsync(e, Context);
        Assert.Equal(1, affected);
    }


    private async Task BuildTestTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(@"CREATE TABLE IF NOT EXISTS {0}Test{1} ({0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
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
