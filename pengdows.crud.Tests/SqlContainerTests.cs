#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
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
    public void AddParameterWithValue_DbParameter_Throws()
    {
        var container = Context.CreateSqlContainer();
        var param = new FakeDbParameter();

        Assert.Throws<ArgumentException>(() => container.AddParameterWithValue(DbType.Int32, param));
    }

    [Fact]
    public void AddParameter_Null_DoesNothing()
    {
        var container = Context.CreateSqlContainer();
        container.AddParameter(null);

        Assert.Equal(0, container.ParameterCount);
    }

    [Fact]
    public void AddParameter_AssignsGeneratedName_WhenMissing()
    {
        var container = Context.CreateSqlContainer();
        var param = new FakeDbParameter { DbType = DbType.Int32, Value = 1 };
        container.AddParameter(param);

        Assert.False(string.IsNullOrEmpty(param.ParameterName));
        Assert.Equal(1, container.ParameterCount);
    }

    [Fact]
    public void SetParameterValue_UpdatesExistingParameter()
    {
        var container = Context.CreateSqlContainer();
        var param = container.AddParameterWithValue(DbType.Int32, 1);

        container.SetParameterValue(param.ParameterName, 5);

        var value = container.GetParameterValue<int>(param.ParameterName);
        Assert.Equal(5, value);
    }

    [Fact]
    public void SetParameterValue_MissingParameter_Throws()
    {
        var container = Context.CreateSqlContainer();

        Assert.Throws<KeyNotFoundException>(() => container.SetParameterValue("does_not_exist", 1));
    }

    [Fact]
    public void GetParameterValue_ReturnsValue()
    {
        var container = Context.CreateSqlContainer();
        var param = container.AddParameterWithValue(DbType.String, "abc");

        var value = container.GetParameterValue<string>(param.ParameterName);
        Assert.Equal("abc", value);
    }

    [Fact]
    public void GetParameterValue_MissingParameter_Throws()
    {
        var container = Context.CreateSqlContainer();

        Assert.Throws<KeyNotFoundException>(() => container.GetParameterValue<int>("missing"));
    }

    [Fact]
    public void GetParameterValue_IntToString_Converts()
    {
        var container = Context.CreateSqlContainer();
        var param = container.AddParameterWithValue(DbType.Int32, 1);

        var value = container.GetParameterValue<string>(param.ParameterName);

        Assert.Equal("1", value);
    }

    [Fact]
    public void GetParameterValue_InvalidConversion_Throws()
    {
        var container = Context.CreateSqlContainer();
        var param = container.AddParameterWithValue(DbType.String, "abc");

        Assert.Throws<InvalidCastException>(() => container.GetParameterValue<int>(param.ParameterName));
    }

    [Fact]
    public void GetParameterValue_Object_ReturnsValue()
    {
        var container = Context.CreateSqlContainer();
        var param = container.AddParameterWithValue(DbType.Int32, 2);

        var value = container.GetParameterValue(param.ParameterName);
        Assert.Equal(2, value);
    }

    [Fact]
    public void GetParameterValue_Object_MissingParameter_Throws()
    {
        var container = Context.CreateSqlContainer();

        Assert.Throws<KeyNotFoundException>(() => container.GetParameterValue("missing"));
    }

    [Fact]
    public void Clear_RemovesQueryAndParameters()
    {
        var container = Context.CreateSqlContainer();
        container.Query.Append("SELECT 1");
        container.AddParameterWithValue(DbType.Int32, 1);

        container.Clear();

        Assert.Equal(string.Empty, container.Query.ToString());
        Assert.Equal(0, container.ParameterCount);
    }

    [Fact]
    public void AddParameterWithValue_UnsupportedDirectionThrows()
    {
        var container = Context.CreateSqlContainer();

        Assert.Throws<ArgumentException>(() =>
            container.AddParameterWithValue("p1", DbType.Int32, 1, ParameterDirection.Output));
    }

    [Fact]
    public void AddParameterWithValue_SetsExplicitInputDirection()
    {
        var container = Context.CreateSqlContainer();
        var param = container.AddParameterWithValue("p1", DbType.Int32, 1, ParameterDirection.Input);

        Assert.Equal(ParameterDirection.Input, param.Direction);
    }

    [Fact]
    public void AddParameterWithValue_DefaultsDirectionToInput()
    {
        var container = Context.CreateSqlContainer();
        var param = container.AddParameterWithValue("p1", DbType.Int32, 1);

        Assert.Equal(ParameterDirection.Input, param.Direction);
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
{0}Test{1} ({0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
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

    public void Dispose_ClearsQueryAndParameters()
    {
        var container = Context.CreateSqlContainer();
        var param = new FakeDbParameter { ParameterName = "p", DbType = DbType.Int32, Value = 1 };
        container.AddParameter(param);
        container.Query.Append("SELECT 1");

        container.Dispose();

        Assert.True(container.IsDisposed);
        Assert.Equal(0, container.ParameterCount);
        Assert.Equal(string.Empty, container.Query.ToString());
    }

    [Fact]
    public async Task DisposeAsync_ClearsQueryAndParameters()
    {
        var container = Context.CreateSqlContainer();
        var param = new FakeDbParameter { ParameterName = "p", DbType = DbType.Int32, Value = 1 };
        container.AddParameter(param);
        container.Query.Append("SELECT 1");

        await container.DisposeAsync();

        Assert.True(container.IsDisposed);
        Assert.Equal(0, container.ParameterCount);
        Assert.Equal(string.Empty, container.Query.ToString());
    }

    [Fact]
    public void AppendQuery_AppendsSqlAndReturnsContainer()
    {
        var container = Context.CreateSqlContainer();
        var result = container.AppendQuery("SELECT 1");

        Assert.Same(container, result);
        Assert.Equal("SELECT 1", container.Query.ToString());
    }

    [Fact]
    public void QuoteProperties_ExposeUnderlyingContextValues()
    {
        var container = Context.CreateSqlContainer();
        Assert.Equal(Context.QuotePrefix, container.QuotePrefix);
        Assert.Equal(Context.QuoteSuffix, container.QuoteSuffix);
        Assert.Equal(Context.CompositeIdentifierSeparator, container.CompositeIdentifierSeparator);
        Assert.NotEqual("[", container.QuotePrefix);
        Assert.NotEqual("]", container.QuoteSuffix);
        Assert.NotEqual("/", container.CompositeIdentifierSeparator);
    }

    [Fact]
    public void WrapForStoredProc_ExecStyle_IncludesParameters()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}", factory);
        var container = ctx.CreateSqlContainer("dbo.my_proc");
        var param = container.AddParameterWithValue(DbType.Int32, 1);
        var expectedName = ctx.MakeParameterName(param);

        var result = container.WrapForStoredProc(ExecutionType.Write);

        Assert.Equal($"EXEC dbo.my_proc {expectedName}", result);
    }

    [Fact]
    public void WrapForStoredProc_PostgreSqlRead_UsesSelectSyntax()
    {
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        var container = ctx.CreateSqlContainer("my_proc");
        var param = container.AddParameterWithValue(DbType.Int32, 1);
        var expectedName = ctx.MakeParameterName(param);

        var result = container.WrapForStoredProc(ExecutionType.Read);

        Assert.Equal($"SELECT * FROM my_proc({expectedName})", result);
    }

    [Fact]
    public void WrapForStoredProc_PostgreSqlCaptureReturn_Throws()
    {
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        var container = ctx.CreateSqlContainer("my_proc");

        Assert.Throws<NotSupportedException>(() => container.WrapForStoredProc(ExecutionType.Read, captureReturn: true));
    }

    [Fact]
    public void WrapForStoredProc_FirebirdRead_UsesSelectSyntax()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Firebird);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Firebird}", factory);
        var container = ctx.CreateSqlContainer("dbo.my_proc");
        var param = container.AddParameterWithValue(DbType.Int32, 1);
        var expectedName = ctx.MakeParameterName(param);

        var result = container.WrapForStoredProc(ExecutionType.Read);

        Assert.Equal($"SELECT * FROM dbo.my_proc({expectedName})", result);
    }

    [Fact]
    public void WrapForStoredProc_FirebirdCaptureReturn_Throws()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Firebird);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Firebird}", factory);
        var container = ctx.CreateSqlContainer("dbo.my_proc");

        Assert.Throws<NotSupportedException>(() => container.WrapForStoredProc(ExecutionType.Read, captureReturn: true));
    }

    [Fact]
    public void WrapForStoredProc_NoProcedureName_Throws()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}", factory);
        var container = ctx.CreateSqlContainer();

        Assert.Throws<InvalidOperationException>(() => container.WrapForStoredProc(ExecutionType.Read));
    }

    [Fact]
    public void WrapForStoredProc_UnsupportedStyle_Throws()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}", factory);
        var container = ctx.CreateSqlContainer("my_proc");

        Assert.Throws<NotSupportedException>(() => container.WrapForStoredProc(ExecutionType.Read));
    }

    [Fact]
    public void AddParameter_OutputWithinLimit_Succeeds()
    {
        var info = (DataSourceInformation)Context.DataSourceInfo;
        var prop = typeof(DataSourceInformation).GetProperty("MaxOutputParameters", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var original = info.MaxOutputParameters;
        prop!.SetValue(info, 1);

        var container = Context.CreateSqlContainer();
        var param = new FakeDbParameter { ParameterName = "p0", DbType = DbType.Int32, Direction = ParameterDirection.Output };

        container.AddParameter(param);

        Assert.Equal(1, container.ParameterCount);

        prop.SetValue(info, original);
    }

    [Fact]
    public void AddParameter_OutputExceedsLimit_Throws()
    {
        var info = (DataSourceInformation)Context.DataSourceInfo;
        var prop = typeof(DataSourceInformation).GetProperty("MaxOutputParameters", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var original = info.MaxOutputParameters;
        prop!.SetValue(info, 1);

        var container = Context.CreateSqlContainer();
        var p1 = new FakeDbParameter { ParameterName = "p0", DbType = DbType.Int32, Direction = ParameterDirection.Output };
        container.AddParameter(p1);

        var p2 = new FakeDbParameter { ParameterName = "p1", DbType = DbType.Int32, Direction = ParameterDirection.Output };

        Assert.Throws<InvalidOperationException>(() => container.AddParameter(p2));

        prop.SetValue(info, original);
    }
}