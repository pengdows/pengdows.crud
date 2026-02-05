#region

using System;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGateway_IntegrationTests : RealSqliteContextTestBase, IAsyncLifetime
{
    private readonly TableGateway<TestEntity, int> entityHelper;

    public TableGateway_IntegrationTests()
    {
        // Create an in-memory SQLite connection
        // _connection = new SqliteConnection("Data Source=:memory:");
        // _connection.Open();
        TypeMap.Register<TestEntity>();
        entityHelper = new TableGateway<TestEntity, int>(Context, AuditValueResolver);

        Assert.Equal(DbMode.SingleConnection, Context.ConnectionMode);
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
    public void QuoteProperties_DelegateToContext()
    {
        Assert.Equal(Context.QuotePrefix, entityHelper.QuotePrefix);
        Assert.Equal(Context.QuoteSuffix, entityHelper.QuoteSuffix);
        Assert.Equal(Context.CompositeIdentifierSeparator, entityHelper.CompositeIdentifierSeparator);
        Assert.NotEqual("?", entityHelper.QuotePrefix);
        Assert.NotEqual("?", entityHelper.QuoteSuffix);
        Assert.NotEqual("?", entityHelper.CompositeIdentifierSeparator);
    }

    [Fact]
    public async Task TryUpdateVersion()
    {
        await BuildTestTable();
        var tmp = new TestEntity
        {
            Name = Guid.NewGuid().ToString()
        };
        var c = entityHelper.BuildCreate(tmp);
        await c.ExecuteNonQueryAsync();
        var sc = entityHelper.BuildBaseRetrieve("a");
        var list = await entityHelper.LoadListAsync(sc);

        Assert.True(list.Count > 0);
        var fisrt = list.First();
        fisrt.Name = Guid.NewGuid().ToString();
        var sc1 = await entityHelper.BuildUpdateAsync(fisrt);
        var recordCount = await sc1.ExecuteNonQueryAsync();
        Assert.Equal(1, recordCount);
    }

    [Fact]
    private void AssertProperNumberOfConnectionsForMode()
    {
        switch (Context.ConnectionMode)
        {
            case DbMode.Standard:
                Assert.Equal(0, Context.NumberOfOpenConnections);
                break;
            default:
                Assert.NotEqual(0, Context.NumberOfOpenConnections);
                break;
        }
    }

    private async Task BuildTestTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS
{0}Test{1} ({0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
{0}Name{1} TEXT UNIQUE NOT NULL,
    {0}CreatedBy{1} TEXT NOT NULL DEFAULT 'system',
    {0}CreatedOn{1} TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    {0}LastUpdatedBy{1} TEXT NOT NULL DEFAULT 'system',
    {0}LastUpdatedOn{1} TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
{0}Version{1} INTEGER NOT NULL DEFAULT 0)", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    [Fact]
    public void BuildCreate_SkipsNonWritableId()
    {
        // Arrange
        var typeMap = new TypeMapRegistry();
        typeMap.Register<IdentityTestEntity>(); // assumes you auto-build TableInfo from attributes

        var helper = new TableGateway<IdentityTestEntity, int>(Context, new StubAuditValueResolver("fred"));

        var entity = new IdentityTestEntity { Id = 42, Name = "Hello" };

        // Act
        var container = helper.BuildCreate(entity);
        var sql = container.Query.ToString();

        // Assert
        var columnId = Context.WrapObjectName("Id");
        var columnName = Context.WrapObjectName("Name");
        Assert.DoesNotContain(columnId, sql, StringComparison.OrdinalIgnoreCase); // check it's not in the SQL
        Assert.Contains(columnName, sql, StringComparison.OrdinalIgnoreCase); // check that another field is included
        Assert.StartsWith("INSERT INTO", sql, StringComparison.OrdinalIgnoreCase); // sanity check
    }

    [Fact]
    public void BuildCreate_SkipsNonInsertableId()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<NonInsertableIdEntity>();

        var helper = new TableGateway<NonInsertableIdEntity, int>(Context, new StubAuditValueResolver("fred"));

        var entity = new NonInsertableIdEntity { Id = 1, Name = "Hello" };

        var container = helper.BuildCreate(entity);
        var sql = container.Query.ToString();

        var columnId = Context.WrapObjectName("Id");
        var columnName = Context.WrapObjectName("Name");
        Assert.DoesNotContain(columnId, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(columnName, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCreate_SetsAuditOnFields_WhenNoUserFields()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<AuditOnOnlyEntity>();

        var helper = new TableGateway<AuditOnOnlyEntity, int>(Context, new StubAuditValueResolver("fred"));

        var entity = new AuditOnOnlyEntity { Name = "Hello" };

        helper.BuildCreate(entity);

        Assert.NotEqual(default, entity.CreatedOn);
        Assert.NotEqual(default, entity.LastUpdatedOn);
    }

    [Fact]
    public async Task TryUpdateVersionWithLoadOriginal()
    {
        await BuildTestTable();
        var tmp = new TestEntity
        {
            Name = Guid.NewGuid().ToString()
        };
        var c = entityHelper.BuildCreate(tmp);
        await c.ExecuteNonQueryAsync();
        var sc = entityHelper.BuildBaseRetrieve("a");
        var list = await entityHelper.LoadListAsync(sc);

        Assert.True(list.Count > 0);
        var fisrt = list.First();
        fisrt.Name = Guid.NewGuid().ToString();
        var sc1 = await entityHelper.BuildUpdateAsync(fisrt, true);
        var recordCount = await sc1.ExecuteNonQueryAsync();
        Assert.Equal(1, recordCount);
    }

    [Fact]
    public async Task MapReaderToObject_MapsCorrectly()
    {
        await BuildTestTable();
        var s = Guid.NewGuid().ToString();
        var tmp = new TestEntity { Name = s };
        var create = entityHelper.BuildCreate(tmp);
        await create.ExecuteNonQueryAsync();

        var retrieve = entityHelper.BuildBaseRetrieve(s);
        var list = await entityHelper.LoadListAsync(retrieve);

        Assert.Single(list);
        var loaded = list[0];
        Assert.Equal(s, loaded.Name);
    }

    [Fact]
    public async Task BuildRetrieveListById()
    {
        await BuildTestTable();
        var s = Guid.NewGuid().ToString();
        var tmp = new TestEntity { Name = s };
        var create = entityHelper.BuildCreate(tmp);
        await create.ExecuteNonQueryAsync();

        var retrieve = entityHelper.BuildBaseRetrieve(string.Empty);
        var list = await entityHelper.LoadListAsync(retrieve);
        var listOfIds = list.Select(x => x.Id).ToList();

        var r = entityHelper.BuildRetrieve(listOfIds);
        var r2 = await entityHelper.LoadListAsync(r);

        Assert.True(listOfIds.Count > 0 && r2.Count == listOfIds.Count);
    }

    [Fact]
    public async Task BuildRetrieveListByObject()
    {
        await BuildTestTable();
        var s = Guid.NewGuid().ToString();
        var tmp = new TestEntity { Name = s };
        var create = entityHelper.BuildCreate(tmp);
        await create.ExecuteNonQueryAsync();

        var retrieve = entityHelper.BuildBaseRetrieve(string.Empty);
        var list = (await entityHelper.LoadListAsync(retrieve)).ToList();

        var r = entityHelper.BuildRetrieve(list);
        var r2 = (await entityHelper.LoadListAsync(r)).ToList();

        Assert.True(list.Count > 0 && r2.Count == list.Count);
        var loaded = list[0];
        Assert.Equal(s, loaded.Name);
    }

    [Fact]
    public async Task BuildDeleteTask()
    {
        await BuildTestTable();
        var s = Guid.NewGuid().ToString();
        var tmp = new TestEntity { Name = s };
        var create = entityHelper.BuildCreate(tmp);
        await create.ExecuteNonQueryAsync();
        //var entities = 
        var retrieve = entityHelper.BuildBaseRetrieve("a");
        var x = await entityHelper.LoadListAsync(retrieve);
        var foundList = x.FindAll(itm => itm.Name == s);
        Assert.Single(foundList);
        var found = foundList.First();
        Assert.True(found.Name == tmp.Name);
        entityHelper.BuildDelete(found.Id);
    }

    [Fact]
    public async Task TransactionEntity()
    {
        try
        {
            await BuildTestTable();
            var s = Guid.NewGuid().ToString();
            var tmp = new TestEntity { Name = s };
            var create = entityHelper.BuildCreate(tmp);
            await create.ExecuteNonQueryAsync();

            var ctx = Context.BeginTransaction();
            var tmp2 = new TestEntity { Name = s + "transaction" };
            var tsc = entityHelper.BuildCreate(tmp2, ctx);
            await tsc.ExecuteNonQueryAsync();

            var retrieveInsideTransaction = entityHelper.BuildBaseRetrieve(s, ctx);
            var retrieve = entityHelper.BuildBaseRetrieve(s);
            var listInside = await entityHelper.LoadListAsync(retrieveInsideTransaction); //will be inside transaction
            ctx.Commit();
            var listOutside = await entityHelper.LoadListAsync(retrieve); //will be outside transaction
            //Due to this being a singleconnection, you can't get a writer outside the connection  
            Assert.True(listInside.Count > 1);
            Assert.True(listInside.Count > 1);

            var loaded = listOutside[0];
            Assert.Equal(s, loaded.Name);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}