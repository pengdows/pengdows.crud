#region
using System;
using System.Reflection;
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

    public async Task InitializeAsync()
    {
        await BuildTestTable();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UpsertAsync_PortableInsertAndUpdate()
    {
        var e = new TestEntity { Name = Guid.NewGuid().ToString() };
        var method = typeof(EntityHelper<TestEntity, int>).GetMethod("UpsertPortableAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task<int>)method!.Invoke(_helper, new object[] { e, Context });
        var affected = await task;
        Assert.Equal(1, affected);
        task = (Task<int>)method.Invoke(_helper, new object[] { e, Context });
        affected = await task;
        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task UpsertAsync_NoKey_Throws()
    {
        TypeMap.Register<NoKeyEntity>();
        var helper = new EntityHelper<NoKeyEntity, int>(Context);
        await BuildNoKeyTable();
        var e = new NoKeyEntity { Value = "v" };
        var method = typeof(EntityHelper<NoKeyEntity, int>).GetMethod("UpsertPortableAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            var task = (Task<int>)method!.Invoke(helper, new object[] { e, Context });
            await task;
        });
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

    private async Task BuildNoKeyTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(@"CREATE TABLE IF NOT EXISTS {0}NoKey{1} ({0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
        {0}Value{1} TEXT NOT NULL)", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

}
