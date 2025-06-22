#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SqlContainerTests : SqlLiteContextTestBase
{
    private readonly EntityHelper<TestEntity, int> entityHelper;

    public SqlContainerTests()
    {
        // Create an in-memory SQLite connection
        // _connection = new SqliteConnection("Data Source=:memory:");
        // _connection.Open();
        TypeMap.Register<TestEntity>();
        entityHelper = new EntityHelper<TestEntity, int>(Context, null);
        Assert.Equal(DbMode.SingleConnection, Context.ConnectionMode);
        BuildTestTable();
    }

    public void Dispose()
    {
        Context.Dispose();
    }

    [Fact]
    public void Constructor_WithContext_InitializesQueryEmpty()
    {
        var container = Context.CreateSqlContainer();
        Assert.NotNull(container.Query);
        Assert.Equal("", container.Query.ToString());
    }

    [Fact]
    public void Constructor_WithQuery_InitializesQueryWithValue()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;

        var query = $"SELECT * FROM {qp}Test{qs}";
        var container = Context.CreateSqlContainer(query);
        Assert.Equal(query, container.Query.ToString());
    }

    [Fact]
    public void AppendParameter_GeneratesRandomName_WhenNameIsNull()
    {
        var container = Context.CreateSqlContainer();
        var param = container.AddParameterWithValue(DbType.String, "test");
        Assert.Equal("test", param.Value);
        Assert.Equal(DbType.String, param.DbType);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_InsertsData()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        await BuildTestTable();
        var container = Context.CreateSqlContainer();
        AssertProperNumberOfConnectionsForMode();
        var p = container.AddParameterWithValue(DbType.String, "TestName");
        container.Query.AppendFormat("INSERT INTO {0}Test{1} ({0}Name{1}) VALUES ({2})", qp, qs,
            Context.MakeParameterName(p));

        var result = await container.ExecuteNonQueryAsync();

        Assert.Equal(1, result); // 1 row affected
    }

    [Fact]
    public async Task ExecuteScalarAsync_ReturnsValue_WhenRowExists()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        await BuildTestTable();
        var container = Context.CreateSqlContainer();
        AssertProperNumberOfConnectionsForMode();
        var p = container.AddParameterWithValue(DbType.String, "TestName");
        container.Query.AppendFormat("INSERT INTO {0}Test{1} ({0}Name{1}) VALUES ({2})", qp, qs,
            Context.MakeParameterName(p));
        await container.ExecuteNonQueryAsync();
        container.Clear();
        container.Query.AppendFormat("SELECT {0}Name{1} FROM {0}Test{1} WHERE {0}Id{1} = 1", qp, qs);

        var result = await container.ExecuteScalarAsync<string>();

        Assert.Equal("TestName", result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_ThrowsException_WhenNoRows()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        await BuildTestTable();
        var container = Context.CreateSqlContainer();

        container.Query.AppendFormat("SELECT {0}Name{1} FROM {0}Test{1} WHERE {0}Id{1} = 1", qp, qs);

        await Assert.ThrowsAsync<InvalidOperationException>(() => container.ExecuteScalarAsync<string>());
        AssertProperNumberOfConnectionsForMode();
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
{0}Test{1} ({0}Id{1} INTEGER PRIMARY KEY, 
{0}Name{1} TEXT,
{0}Version{1} INTEGER NOT NULL DEFAULT 0)", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ExecuteReaderAsync_ReturnsData()
    {
        await BuildTestTable();
        var container = Context.CreateSqlContainer();
        AssertProperNumberOfConnectionsForMode();
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var p = container.AddParameterWithValue(DbType.String, "TestName");
        container.Query.AppendFormat("INSERT INTO {0}Test{1} ({0}Name{1}) VALUES ({2})", qp, qs,
            Context.MakeParameterName(p));
        await container.ExecuteNonQueryAsync();
        AssertProperNumberOfConnectionsForMode();
        container.Clear();
        container.Query.AppendFormat("SELECT {0}Name{1} FROM {0}Test{1}", qp, qs);

        await using var reader = await container.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("TestName", reader.GetString(0));
        Assert.False(await reader.ReadAsync());
        AssertProperNumberOfConnectionsForMode();
    }
}