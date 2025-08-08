using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class EntityHelperNegativeTests : SqlLiteContextTestBase
{
    private readonly EntityHelper<TestEntity, int> helper;

    public EntityHelperNegativeTests()
    {
        TypeMap.Register<TestEntity>();
        helper = new EntityHelper<TestEntity, int>(Context);
    }

    [Fact]
    public async Task BuildUpdateAsync_NullEntity_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await helper.BuildUpdateAsync(null!));
    }

    [Fact]
    public async Task BuildUpdateAsync_LoadOriginal_NotFound_Throws()
    {
        await BuildTestTable();
        var entity = new TestEntity { Id = 123, Name = "missing" };
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await helper.BuildUpdateAsync(entity, true));
    }

    [Fact]
    public async Task BuildUpdateAsync_NoChanges_Throws()
    {
        await BuildTestTable();
        var newEntity = new TestEntity { Name = Guid.NewGuid().ToString() };
        await helper.CreateAsync(newEntity, Context);
        var loaded = (await helper.LoadListAsync(helper.BuildBaseRetrieve("a")))[0];
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await helper.BuildUpdateAsync(loaded, true));
    }

    [Fact]
    public void BuildWhereByPrimaryKey_NullList_Throws()
    {
        var sc = Context.CreateSqlContainer();
        Assert.Throws<ArgumentException>(() => helper.BuildWhereByPrimaryKey(null, sc));
    }

    [Fact]
    public void BuildWhereByPrimaryKey_TooManyParams_Throws()
    {
        var sc = Context.CreateSqlContainer();
        var info = (DataSourceInformation)Context.DataSourceInfo;
        var prop = typeof(DataSourceInformation).GetProperty("MaxParameterLimit", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var original = info.MaxParameterLimit;
        prop!.SetValue(info, 2);

        var list = new List<TestEntity>
        {
            new() { Id = 1, Name = "A" },
            new() { Id = 2, Name = "B" },
            new() { Id = 3, Name = "C" }
        };

        Assert.Throws<TooManyParametersException>(() => helper.BuildWhereByPrimaryKey(list, sc));
        prop.SetValue(info, original);
    }

    [Fact]
    public void BuildUpsert_NullEntity_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => helper.BuildUpsert(null!));
    }

    [Fact]
    public void BuildUpsert_UnsupportedDatabase_Throws()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
        TypeMap.Register<TestEntity>();
        var localHelper = new EntityHelper<TestEntity, int>(context);

        var prop = typeof(DataSourceInformation).GetProperty("Product", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        prop.SetValue(context.DataSourceInfo, SupportedDatabase.Unknown);

        var entity = new TestEntity { Id = 1, Name = "foo" };

        Assert.Throws<NotSupportedException>(() => localHelper.BuildUpsert(entity));
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
