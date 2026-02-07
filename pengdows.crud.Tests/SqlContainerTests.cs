#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SqlContainerTests : SqlLiteContextTestBase, IDisposable
{
    private readonly TableGateway<TestEntity, int> entityHelper;

    public SqlContainerTests()
    {
        // Create an in-memory SQLite connection
        // _connection = new SqliteConnection("Data Source=:memory:");
        // _connection.Open();
        TypeMap.Register<TestEntity>();
        entityHelper = new TableGateway<TestEntity, int>(Context);
        Assert.Equal(DbMode.SingleConnection, Context.ConnectionMode);
        BuildTestTable().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Dispose_ReturnsParametersToDialectPool()
    {
        var param = Context.CreateDbParameter("p0", DbType.Int32, 1);
        var container = Context.CreateSqlContainer();
        container.AddParameter(param);

        await container.DisposeAsync();

        var reused = Context.CreateDbParameter("p1", DbType.Int32, 2);

        Assert.Same(param, reused);
        Assert.Equal(2, reused.Value);
    }

    void IDisposable.Dispose()
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
        var param = new fakeDbParameter();

        Assert.Throws<ArgumentException>(() => container.AddParameterWithValue(DbType.Int32, param));
    }

    [Fact]
    public void AddParameter_Null_DoesNothing()
    {
        var container = Context.CreateSqlContainer();
        container.AddParameter(null!);

        Assert.Equal(0, container.ParameterCount);
    }

    [Fact]
    public void AddParameter_AssignsGeneratedName_WhenMissing()
    {
        var container = Context.CreateSqlContainer();
        var param = new fakeDbParameter { DbType = DbType.Int32, Value = 1 };
        container.AddParameter(param);

        Assert.False(string.IsNullOrEmpty(param.ParameterName));
        Assert.Equal(1, container.ParameterCount);
    }

    [Fact]
    public void GeneratedParameterNames_AreDeterministic_AndWithinLimit()
    {
        var container = Context.CreateSqlContainer();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 32; i++)
        {
            var param = container.AddParameterWithValue(DbType.Int32, i);
            Assert.StartsWith("p", param.ParameterName, StringComparison.OrdinalIgnoreCase);
            Assert.True(param.ParameterName.Length <= Context.DataSourceInfo.ParameterNameMaxLength);
            Assert.True(seen.Add(param.ParameterName));
        }
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
    public void SetParameterValue_AllPrefixesNormalize()
    {
        var container = Context.CreateSqlContainer();
        var param = container.AddParameterWithValue(DbType.Int32, 1);

        container.SetParameterValue("@" + param.ParameterName, 2);
        container.SetParameterValue(":" + param.ParameterName, 3);
        container.SetParameterValue("?" + param.ParameterName, 4);
        container.SetParameterValue("$" + param.ParameterName, 5);

        var value = container.GetParameterValue<int>(param.ParameterName);
        Assert.Equal(5, value);
    }

    [Fact]
    public void SetParameterValue_AlternatePrefixFallbacks()
    {
        var container = Context.CreateSqlContainer();
        var param = container.AddParameterWithValue(DbType.Int32, 1);

        var alternate = (param.ParameterName[0] == 'p' ? 'w' : 'p') + param.ParameterName.Substring(1);
        container.SetParameterValue(alternate, 7);

        var value = container.GetParameterValue<int>(param.ParameterName);
        Assert.Equal(7, value);
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
    public async Task ExecuteScalarAsync_ReturnsNull_WhenNoRowsForNullableType()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        await BuildTestTable();
        var container = Context.CreateSqlContainer();

        container.Query.AppendFormat("SELECT {0}Name{1} FROM {0}Test{1} WHERE {0}Id{1} = 1", qp, qs);

        var result = await container.ExecuteScalarAsync<string>();
        Assert.Null(result);
        AssertProperNumberOfConnectionsForMode();
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_ReturnsValueTaskResult()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        await BuildTestTable();
        var container = Context.CreateSqlContainer();
        AssertProperNumberOfConnectionsForMode();
        var p = container.AddParameterWithValue(DbType.String, "Named");
        container.Query.AppendFormat("INSERT INTO {0}Test{1} ({0}Name{1}) VALUES ({2})", qp, qs,
            Context.MakeParameterName(p));

        ValueTask<int> valueTask = container.ExecuteNonQueryAsync();

        var result = await valueTask;

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_ValueTaskCanBeAwaited()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        await BuildTestTable();
        var container = Context.CreateSqlContainer();
        AssertProperNumberOfConnectionsForMode();
        var insertParam = container.AddParameterWithValue(DbType.String, "TestName");
        container.Query.AppendFormat("INSERT INTO {0}Test{1} ({0}Name{1}) VALUES ({2})", qp, qs,
            Context.MakeParameterName(insertParam));
        await container.ExecuteNonQueryAsync();
        container.Clear();
        container.Query.AppendFormat("SELECT {0}Name{1} FROM {0}Test{1} WHERE {0}Id{1} = 1", qp, qs);

        ValueTask<string?> valueTask = container.ExecuteScalarAsync<string>();

        var result = await valueTask;
        Assert.Equal("TestName", result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_ThrowsException_WhenNoRowsForNonNullableType()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        await BuildTestTable();
        var container = Context.CreateSqlContainer();

        container.Query.AppendFormat("SELECT {0}Id{1} FROM {0}Test{1} WHERE {0}Id{1} = -1", qp, qs);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await container.ExecuteScalarAsync<int>());
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

    [Fact]
    public void Dispose_ClearsQueryAndParameters()
    {
        var container = Context.CreateSqlContainer();
        var param = new fakeDbParameter { ParameterName = "p", DbType = DbType.Int32, Value = 1 };
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
        var param = new fakeDbParameter { ParameterName = "p", DbType = DbType.Int32, Value = 1 };
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

    [Theory]
    [InlineData(SupportedDatabase.SqlServer, "dbo.my_proc", ExecutionType.Write, "EXEC \"dbo\".\"my_proc\" {0}")]
    [InlineData(SupportedDatabase.PostgreSql, "my_proc", ExecutionType.Read, "SELECT * FROM \"my_proc\"({0})")]
    [InlineData(SupportedDatabase.Firebird, "dbo.my_proc", ExecutionType.Read,
        "SELECT * FROM \"dbo\".\"my_proc\"({0})")]
    public void WrapForStoredProc_ByProvider_FormatsCorrectly(
        SupportedDatabase product,
        string procName,
        ExecutionType executionType,
        string format)
    {
        var factory = new fakeDbFactory(product);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var container = ctx.CreateSqlContainer(procName);
        var param = container.AddParameterWithValue(DbType.Int32, 1);
        var expectedName = ctx.MakeParameterName(param);

        var result = container.WrapForStoredProc(executionType);
        var expected = string.Format(format, expectedName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Firebird)]
    public void WrapForStoredProc_CaptureReturn_ThrowsForNonExec(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var container = ctx.CreateSqlContainer("my_proc");
        Assert.Throws<NotSupportedException>(() =>
            container.WrapForStoredProc(ExecutionType.Read, captureReturn: true));
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    public void WrapForStoredProc_Unsupported_Throws(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var container = ctx.CreateSqlContainer("my_proc");
        Assert.Throws<NotSupportedException>(() => container.WrapForStoredProc(ExecutionType.Read));
    }

    [Fact]
    public void WrapForStoredProc_NoProcedureName_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}", factory);
        var container = ctx.CreateSqlContainer();
        Assert.Throws<InvalidOperationException>(() => container.WrapForStoredProc(ExecutionType.Read));
    }

    [Fact]
    public void AddParameter_OutputWithinLimit_Succeeds()
    {
        var info = (DataSourceInformation)Context.DataSourceInfo;
        var prop = typeof(DataSourceInformation).GetProperty("MaxOutputParameters",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var original = info.MaxOutputParameters;
        prop!.SetValue(info, 1);

        var container = Context.CreateSqlContainer();
        var param = new fakeDbParameter
            { ParameterName = "p0", DbType = DbType.Int32, Direction = ParameterDirection.Output };

        container.AddParameter(param);

        Assert.Equal(1, container.ParameterCount);

        prop.SetValue(info, original);
    }

    [Fact]
    public void AddParameter_OutputExceedsLimit_Throws()
    {
        var info = (DataSourceInformation)Context.DataSourceInfo;
        var prop = typeof(DataSourceInformation).GetProperty("MaxOutputParameters",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var original = info.MaxOutputParameters;
        prop!.SetValue(info, 1);

        var container = Context.CreateSqlContainer();
        var p1 = new fakeDbParameter
            { ParameterName = "p0", DbType = DbType.Int32, Direction = ParameterDirection.Output };
        container.AddParameter(p1);

        var p2 = new fakeDbParameter
            { ParameterName = "p1", DbType = DbType.Int32, Direction = ParameterDirection.Output };

        Assert.Throws<InvalidOperationException>(() => container.AddParameter(p2));

        prop.SetValue(info, original);
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithLoggingDisabled_DoesNotLogSql()
    {
        var mockLogger = new TestLogger();
        mockLogger.SetLogLevel(LogLevel.Critical); // Disable info logging
        var loggerFactory = new TestLoggerFactory(mockLogger);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        await using var context = new DatabaseContext(config, SqliteFactory.Instance, loggerFactory, TypeMap);
        await using var container = context.CreateSqlContainer("SELECT 1");
        await using var reader = await container.ExecuteReaderAsync();

        Assert.Empty(mockLogger.LogEntries);
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithLoggingEnabled_LogsSqlExecution()
    {
        var mockLogger = new TestLogger();
        mockLogger.SetLogLevel(LogLevel.Information); // Enable info logging
        var loggerFactory = new TestLoggerFactory(mockLogger);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        await using var context = new DatabaseContext(config, SqliteFactory.Instance, loggerFactory, TypeMap);
        await using var container = context.CreateSqlContainer("SELECT 42");
        await using var reader = await container.ExecuteReaderAsync();
        // Multiple info logs can occur during initialization; just ensure something was logged at Info level
        Assert.True(mockLogger.LogEntries.Count >= 1);
    }

    [Fact]
    public async Task Dispose_WithActiveReader_LogsWarning_AndSkipsPooling()
    {
        var mockLogger = new TestLogger();
        mockLogger.SetLogLevel(LogLevel.Warning);
        var loggerFactory = new TestLoggerFactory(mockLogger);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        var typeMap = new TypeMapRegistry();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext(config, factory, loggerFactory, typeMap);

        await using var container = context.CreateSqlContainer("SELECT 1");
        var param = container.AddParameterWithValue("p0", DbType.Int32, 1);

        var reader = await container.ExecuteReaderAsync();

        container.Dispose();

        Assert.Contains(mockLogger.LogEntries, entry => entry.Contains("skipping parameter pooling"));

        var newParam = context.CreateDbParameter("p1", DbType.Int32, 2);
        Assert.NotSame(param, newParam);

        await reader.DisposeAsync();

        var pooled = context.CreateDbParameter("p2", DbType.Int32, 3);
        Assert.Same(param, pooled);
    }

    [Fact]
    public async Task Dispose_WithoutReader_ReturnsParametersToPoolImmediately()
    {
        var param = Context.CreateDbParameter("p0", DbType.Int32, 1);
        var container = Context.CreateSqlContainer();
        container.AddParameter(param);

        await container.DisposeAsync();

        var reused = Context.CreateDbParameter("p1", DbType.Int32, 2);

        Assert.Same(param, reused);
        Assert.Equal(2, reused.Value);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithStoredProcedure_OnSqlite_ThrowsNotSupported()
    {
        var mockLogger = new TestLogger();
        mockLogger.SetLogLevel(LogLevel.Information);
        var loggerFactory = new TestLoggerFactory(mockLogger);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        await using var context = new DatabaseContext(config, SqliteFactory.Instance, loggerFactory, TypeMap);
        await using var container = context.CreateSqlContainer("MyStoredProc");
        container.AddParameterWithValue("param1", DbType.String, "value1");

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await container.ExecuteNonQueryAsync(CommandType.StoredProcedure));
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_LogsParameterMetadata_WhenDebugEnabled()
    {
        var mockLogger = new TestLogger();
        mockLogger.SetLogLevel(LogLevel.Debug);
        var loggerFactory = new TestLoggerFactory(mockLogger);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        await using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer),
            loggerFactory, TypeMap);
        await using var container = context.CreateSqlContainer("SELECT @p0");
        container.AddParameterWithValue("p0", DbType.String, "value");

        await container.ExecuteNonQueryAsync();

        Assert.Contains(mockLogger.LogEntries, entry => entry.StartsWith("Parameters:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_DoesNotLogParameters_WhenNoneProvided()
    {
        var mockLogger = new TestLogger();
        mockLogger.SetLogLevel(LogLevel.Debug);
        var loggerFactory = new TestLoggerFactory(mockLogger);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        await using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer),
            loggerFactory, TypeMap);
        await using var container = context.CreateSqlContainer("SELECT 1");

        await container.ExecuteNonQueryAsync();

        Assert.DoesNotContain(mockLogger.LogEntries, entry => entry.StartsWith("Parameters:", StringComparison.Ordinal));
    }

    // removed: consolidated with parameterized tests above

    private class TestLogger : ILogger
    {
        public List<string> LogEntries { get; } = new();
        private LogLevel _minLogLevel = LogLevel.Information;

        public void SetLogLevel(LogLevel level)
        {
            _minLogLevel = level;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= _minLogLevel;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                LogEntries.Add(formatter(state, exception));
            }
        }
    }

    private class TestLoggerFactory : ILoggerFactory
    {
        private readonly ILogger _logger;

        public TestLoggerFactory(ILogger logger)
        {
            _logger = logger;
        }

        public void Dispose()
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _logger;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
