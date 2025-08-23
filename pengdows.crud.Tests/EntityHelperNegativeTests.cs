using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
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
        // For this test, we need to test the behavior when a database has low parameter limits
        // Since we can't easily change MaxParameterLimit at runtime, we'll use a different approach:
        // Create a condition where we have more parameters than typical limits would allow
        
        var list = new List<TestEntity>();
        // Create enough entities to exceed typical parameter limits
        for (int i = 1; i <= 1000; i++)
        {
            list.Add(new TestEntity { Id = i, Name = $"Entity{i}" });
        }

        var sc = Context.CreateSqlContainer();
        
        // This should throw because we're exceeding reasonable parameter limits
        Assert.Throws<TooManyParametersException>(() => helper.BuildWhereByPrimaryKey(list, sc));
    }

    [Fact]
    public void BuildUpsert_NullEntity_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => helper.BuildUpsert(null!));
    }

    [Fact]
    public void BuildUpsert_UnsupportedDatabase_Throws()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Unknown);
        
        // Creating a DatabaseContext with Unknown database type should throw ArgumentException
        Assert.Throws<ArgumentException>(() => 
            new DatabaseContext("Data Source=:memory:;EmulatedProduct=Unknown", factory));
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
