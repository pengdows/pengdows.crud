using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using pengdows.crud.metrics;
using pengdows.crud.threading;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class CoveragePush_DatabaseContextLifecycleInitializationTests
{
    private static readonly BindingFlags AnyInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags AnyStatic = BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly MethodInfo GetConnectionOpenLockMethod =
        typeof(DatabaseContext).GetMethod("GetConnectionOpenLock", AnyInstance)!;

    private static readonly MethodInfo ResolveDataSourceMethod =
        typeof(DatabaseContext).GetMethod("ResolveDataSource", AnyInstance)!;

    private static readonly MethodInfo AcquireSlotMethod =
        typeof(DatabaseContext).GetMethod("AcquireSlot", AnyInstance)!;

    private static readonly MethodInfo ShouldIgnoreKeyMethod =
        typeof(DatabaseContext).GetMethod("ShouldIgnoreKey", AnyStatic)!;

    private static readonly MethodInfo TryParseReadOnlyParameterMethod =
        typeof(DatabaseContext).GetMethod("TryParseReadOnlyParameter", AnyStatic)!;

    private static readonly MethodInfo SensitiveValuesStrippedMethod =
        typeof(DatabaseContext).GetMethod("SensitiveValuesStripped", AnyStatic)!;

    private static readonly MethodInfo TryExtractSensitiveValuesMethod =
        typeof(DatabaseContext).GetMethod("TryExtractSensitiveValues", AnyStatic)!;

    private static readonly MethodInfo ResolveSharedMaxMethod =
        typeof(DatabaseContext).GetMethod("ResolveSharedMax", AnyStatic)!;

    private static readonly MethodInfo ResolveGovernorMaxMethod =
        typeof(DatabaseContext).GetMethods(AnyStatic).Single(m => m.Name == "ResolveGovernorMax");

    private static readonly MethodInfo AreEquivalentConnectionStringsMethod =
        typeof(DatabaseContext).GetMethod("AreConnectionStringsEquivalentIgnoringCredentials", AnyStatic)!;

    private static readonly MethodInfo RedactConnectionStringMethod =
        typeof(DatabaseContext).GetMethod("RedactConnectionString", AnyStatic)!;

    private static readonly MethodInfo RepresentsRawConnectionStringMethod =
        typeof(DatabaseContext).GetMethod("RepresentsRawConnectionString", AnyStatic)!;

    private static readonly MethodInfo CanUseForApplicationNameMethod =
        typeof(DatabaseContext).GetMethod("CanUseForApplicationName", AnyStatic)!;

    private static readonly MethodInfo NormalizeConnectionStringMethod =
        typeof(DatabaseContext).GetMethod("NormalizeConnectionString", AnyInstance)!;

    private static readonly MethodInfo GetFactoryConnectionStringBuilderStaticMethod =
        typeof(DatabaseContext).GetMethod("GetFactoryConnectionStringBuilderStatic", AnyStatic)!;

    private static readonly MethodInfo TryBuildNormalizedConnectionMapMethod =
        typeof(DatabaseContext).GetMethod("TryBuildNormalizedConnectionMap", AnyStatic)!;

    private static readonly MethodInfo LogModeOverrideMethod =
        typeof(DatabaseContext).GetMethod("LogModeOverride", AnyInstance)!;

    [Fact]
    public void Lifecycle_GetConnectionOpenLock_WhenSerializationDisabled_ReturnsNoOpLocker()
    {
        using var context = CreateSqliteContext();

        var locker = GetConnectionOpenLockMethod.Invoke(context, null);

        Assert.IsType<NoOpAsyncLocker>(locker);
    }

    [Fact]
    public void Lifecycle_SessionSettingsFields_ArePopulatedDuringInitialization()
    {
        using var context = CreateSqliteContext();

        var readOnlySettings = typeof(DatabaseContext).GetField("_cachedReadOnlySessionSettings", AnyInstance)!.GetValue(context);
        var readWriteSettings = typeof(DatabaseContext).GetField("_cachedReadWriteSessionSettings", AnyInstance)!.GetValue(context);

        Assert.NotNull(readOnlySettings);
        Assert.NotNull(readWriteSettings);
    }

    [Fact]
    public void Lifecycle_ResolveDataSource_ReadOnlyBranch_ReturnsNullWhenReaderDataSourceMissing()
    {
        var context = (DatabaseContext)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseContext));
        SetField(context, "_connectionString", "Data Source=writer.db");
        SetField(context, "_readerConnectionString", "Data Source=reader.db");
        SetField(context, "_dataSource", new StubDataSource("Data Source=writer.db"));
        SetField(context, "_readerDataSource", null);
        SetField(context, "_dataSourceProvided", true);

        var resolved = ResolveDataSourceMethod.Invoke(context, new object[] { true });

        Assert.Null(resolved);
    }

    [Fact]
    public void Lifecycle_AcquireSlot_WhenGovernorMissing_ReturnsDefaultSlot()
    {
        var context = (DatabaseContext)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseContext));
        SetField(context, "_effectivePoolGovernorEnabled", true);
        SetField(context, "_attributionStats", new AttributionStats());
        SetField(context, "_readerGovernor", null);
        SetField(context, "_writerGovernor", null);

        var slot = AcquireSlotMethod.Invoke(context, new object[] { ExecutionType.Read });

        Assert.NotNull(slot);
    }

    [Fact]
    public void Lifecycle_ExecuteSessionSettings_NonDbConnection_ReturnsImmediately()
    {
        using var context = CreateSqliteContext();
        using var connection = new NonDbConnection();

        context.ExecuteSessionSettings(connection, readOnly: true);
    }

    [Fact]
    public void InitializationHelpers_CoverPrivateStaticBranches()
    {
        Assert.True((bool)ShouldIgnoreKeyMethod.Invoke(null, new object[] { "pwd" })!);
        Assert.True((bool)ShouldIgnoreKeyMethod.Invoke(null, new object[] { "uid" })!);
        Assert.True((bool)ShouldIgnoreKeyMethod.Invoke(null, new object[] { "user" })!);
        Assert.True((bool)ShouldIgnoreKeyMethod.Invoke(null, new object[] { "username" })!);

        var parseArgs = new object?[] { "ApplicationIntent", null, null };
        var parsed = (bool)TryParseReadOnlyParameterMethod.Invoke(null, parseArgs)!;
        Assert.False(parsed);
        Assert.Null(parseArgs[1]);
        Assert.Null(parseArgs[2]);

        Assert.False((bool)SensitiveValuesStrippedMethod.Invoke(null, new object[] { " ", "normalized" })!);
        Assert.True((bool)SensitiveValuesStrippedMethod.Invoke(
            null,
            new object[] { "Server=prod;Password=secret", string.Empty })!);

        var extractArgs = new object?[] { "Server=prod;Password=\"", null };
        var extracted = (bool)TryExtractSensitiveValuesMethod.Invoke(null, extractArgs)!;
        Assert.False(extracted);

        Assert.Null(ResolveSharedMaxMethod.Invoke(null, new object?[] { null, null }));
        Assert.Equal(5, ResolveSharedMaxMethod.Invoke(null, new object?[] { null, 5 }));
        Assert.Equal(7, ResolveSharedMaxMethod.Invoke(null, new object?[] { 7, null }));

        var noMaxConfig = new PoolConfig(null, null, null, PoolConfigSource.DialectDefault);
        Assert.Null(ResolveGovernorMaxMethod.Invoke(null, new object?[] { null, noMaxConfig }));
        var withMaxConfig = new PoolConfig(null, null, 9, PoolConfigSource.ConnectionString);
        Assert.Equal(9, ResolveGovernorMaxMethod.Invoke(null, new object?[] { null, withMaxConfig }));

        var equivalentWhenEmpty = (bool)AreEquivalentConnectionStringsMethod.Invoke(null, new object?[]
        {
            string.Empty,
            "Server=prod;Database=db",
            null,
            null,
            "-ro"
        })!;
        Assert.False(equivalentWhenEmpty);

        var redactedEmpty = Assert.IsType<string>(RedactConnectionStringMethod.Invoke(null, new object?[] { "   " }));
        Assert.Equal(string.Empty, redactedEmpty);

        var rawBuilder = new DbConnectionStringBuilder();
        rawBuilder["Data Source"] = "Data Source=raw-value";
        var cannotUseForAppName = (bool)CanUseForApplicationNameMethod.Invoke(
            null,
            new object[] { rawBuilder, "Data Source=raw-value" })!;
        Assert.False(cannotUseForAppName);

        var representsRaw = (bool)RepresentsRawConnectionStringMethod.Invoke(
            null,
            new object?[] { null, "Data Source=raw-value" })!;
        Assert.True(representsRaw);
    }

    [Fact]
    public void InitializationHelpers_CoverAdditionalNormalizationAndModeBranches()
    {
        var context = (DatabaseContext)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseContext));

        var whitespaceNormalized = Assert.IsType<string>(
            NormalizeConnectionStringMethod.Invoke(context, new object[] { "   " }));
        Assert.Equal("   ", whitespaceNormalized);

        SetField(context, "_factory", new ThrowingBuilderFactory());
        var fallbackNormalized = Assert.IsType<string>(
            NormalizeConnectionStringMethod.Invoke(context, new object[] { "Server=prod;Database=db" }));
        Assert.Equal("Server=prod;Database=db", fallbackNormalized);

        var builder = Assert.IsAssignableFrom<DbConnectionStringBuilder>(
            GetFactoryConnectionStringBuilderStaticMethod.Invoke(null, new object[] { "Data Source=test.db" }));
        Assert.Equal("Data Source=test.db", builder.ConnectionString, ignoreCase: true);

        var validMapArgs = new object?[]
        {
            "Server=prod;Database=db;Application Name=app-ro;User Id=u;Password=p",
            "ApplicationIntent",
            "ReadOnly",
            "Application Name",
            "-ro",
            null
        };
        var validMapBuilt = (bool)TryBuildNormalizedConnectionMapMethod.Invoke(null, validMapArgs)!;
        Assert.True(validMapBuilt);

        Assert.False((bool)SensitiveValuesStrippedMethod.Invoke(
            null,
            new object[] { "Server=prod;Password=secret", "Server=prod;Password=secret" })!);

        LogModeOverrideMethod.Invoke(context, new object[] { DbMode.Standard, DbMode.Standard, "same mode" });
    }

    private static DatabaseContext CreateSqliteContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=coverage-lifecycle.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        return new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));
    }

    private static void SetField(object instance, string fieldName, object? value)
    {
        var field = typeof(DatabaseContext).GetField(fieldName, AnyInstance);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private sealed class StubDataSource : DbDataSource
    {
        private readonly string _connectionString;

        public StubDataSource(string connectionString)
        {
            _connectionString = connectionString;
        }

        public override string ConnectionString => _connectionString;

        protected override DbConnection CreateDbConnection()
        {
            var connection = new fakeDbConnection();
            connection.ConnectionString = _connectionString;
            return connection;
        }
    }

    private sealed class NonDbConnection : IDbConnection
    {
        [AllowNull]
        public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => string.Empty;
        public ConnectionState State => ConnectionState.Open;

        public IDbTransaction BeginTransaction()
        {
            throw new NotSupportedException();
        }

        public IDbTransaction BeginTransaction(IsolationLevel il)
        {
            throw new NotSupportedException();
        }

        public void ChangeDatabase(string databaseName)
        {
            throw new NotSupportedException();
        }

        public void Close()
        {
        }

        public IDbCommand CreateCommand()
        {
            throw new NotSupportedException();
        }

        public void Open()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingBuilderFactory : DbProviderFactory
    {
        public override DbConnection CreateConnection()
        {
            return new fakeDbConnection();
        }

        public override DbConnectionStringBuilder CreateConnectionStringBuilder()
        {
            throw new InvalidOperationException("builder failure");
        }
    }

    // =========================================================================
    // String-based provider name constructor (lines 69-83)
    // =========================================================================

    [Fact]
    public void Constructor_StringProviderName_InitializesContext()
    {
        // Register fakeDb factory under a provider name so DbProviderFactories.GetFactory can find it
        const string providerName = "pengdows.crud.fakeDb.test.init";
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        try
        {
            System.Data.Common.DbProviderFactories.RegisterFactory(providerName, factory);
        }
        catch (InvalidOperationException)
        {
            // Already registered from a previous test run — that's fine
        }

        using var ctx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite",
            providerName);

        Assert.NotNull(ctx);
        Assert.NotNull(ctx.GetDialect());
    }

    [Fact]
    public void Constructor_StringProviderName_NullConnectionString_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseContext(null!, "some-provider"));
    }

    [Fact]
    public void Constructor_StringProviderName_NullProviderName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseContext("Data Source=:memory:", (string)null!));
    }
}
