using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
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
    public async Task BuildUpdateAsync_NoAudit_NoChanges_Throws()
    {
        // Use real SQLite for this integration test to ensure proper data persistence
        using var realContext = new DatabaseContext("Data Source=:memory:", Microsoft.Data.Sqlite.SqliteFactory.Instance, new TypeMapRegistry());
        var typeMap = realContext.TypeMapRegistry;
        _ = typeMap.GetTableInfo<NoAuditEntity>();

        var noAuditHelper = new EntityHelper<NoAuditEntity, int>(realContext);
        await BuildNoAuditTableReal(realContext);
        var e = new NoAuditEntity { Name = Guid.NewGuid().ToString() };
        await noAuditHelper.CreateAsync(e, realContext);
        var loaded = await noAuditHelper.RetrieveOneAsync(e);
        Assert.NotNull(loaded);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await noAuditHelper.BuildUpdateAsync(loaded!, true));
    }

    [Fact]
    public void BuildWhereByPrimaryKey_NullList_Throws()
    {
        var sc = Context.CreateSqlContainer();
        Assert.Throws<ArgumentException>("listOfObjects", () => helper.BuildWhereByPrimaryKey(null, sc));
    }

    [Fact]
    public void BuildWhereByPrimaryKey_EmptyList_Throws()
    {
        var sc = Context.CreateSqlContainer();
        Assert.Throws<ArgumentException>("listOfObjects", () => helper.BuildWhereByPrimaryKey(new List<TestEntity>(), sc));
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
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Unknown", factory);

        // BuildUpsert should throw NotSupportedException for unknown database types
        var entityHelper = new EntityHelper<TestEntity, int>(context);
        var testEntity = new TestEntity { Id = 1, Name = "Test" };
        Assert.Throws<NotSupportedException>(() =>
            entityHelper.BuildUpsert(testEntity));
    }

    [Fact]
    public void BuildCreate_NullEntity_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => helper.BuildCreate(null!));
    }

    [Fact]
    public void BuildDelete_NoIdColumn_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => TypeMap.Register<EntityWithoutId>());
    }

    [Fact]
    public void BuildCreate_NoIdColumn_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => TypeMap.Register<EntityWithoutId>());
    }

    [Table("NoIdTable")]
    private class EntityWithoutId
    {
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;
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

    private async Task BuildNoAuditTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}NoAudit{1} ({0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,{0}Name{1} TEXT NOT NULL)", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private async Task BuildNoAuditTableReal(IDatabaseContext context)
    {
        var qp = context.QuotePrefix;
        var qs = context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}NoAudit{1} ({0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,{0}Name{1} TEXT NOT NULL)", qp, qs);
        var container = context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    [Table("NoAudit")]
    private class NoAuditEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }
}
