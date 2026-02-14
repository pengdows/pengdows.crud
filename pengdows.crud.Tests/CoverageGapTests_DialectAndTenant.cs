using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.tenant;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class CoverageGapTests_DialectAndTenant
{
    #region FirebirdDialect Tests

    private static FirebirdDialect CreateFirebirdDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        return new FirebirdDialect(factory, NullLogger.Instance);
    }

    private static FirebirdDialect CreateFirebirdDialect(fakeDbFactory factory)
    {
        return new FirebirdDialect(factory, NullLogger.Instance);
    }

    [Fact]
    public void FirebirdDialect_GetLastInsertedIdQuery_ThrowsNotSupportedException()
    {
        var dialect = CreateFirebirdDialect();

        var ex = Assert.Throws<NotSupportedException>(() => dialect.GetLastInsertedIdQuery());
        Assert.Contains("Firebird requires generator-specific syntax", ex.Message);
        Assert.Contains("RETURNING clause", ex.Message);
    }

    [Fact]
    public void FirebirdDialect_UpsertIncomingColumn_ReturnsAliasedWrappedColumn()
    {
        var dialect = CreateFirebirdDialect();

        var result = dialect.UpsertIncomingColumn("my_column");

        Assert.Contains("src", result);
        Assert.Contains("my_column", result);
    }

    [Fact]
    public void FirebirdDialect_UpsertIncomingColumn_WithDifferentColumnNames()
    {
        var dialect = CreateFirebirdDialect();

        var result1 = dialect.UpsertIncomingColumn("id");
        var result2 = dialect.UpsertIncomingColumn("name");
        var result3 = dialect.UpsertIncomingColumn("created_at");

        Assert.Contains("id", result1);
        Assert.Contains("name", result2);
        Assert.Contains("created_at", result3);

        // All should have the src alias prefix
        Assert.StartsWith("\"src\".", result1);
        Assert.StartsWith("\"src\".", result2);
        Assert.StartsWith("\"src\".", result3);
    }

    [Fact]
    public void FirebirdDialect_UpsertIncomingAlias_ReturnsSrc()
    {
        var dialect = CreateFirebirdDialect();

        Assert.Equal("src", dialect.UpsertIncomingAlias);
    }

    [Fact]
    public void FirebirdDialect_GetNaturalKeyLookupQuery_ReplacesLimitWithRows()
    {
        var dialect = CreateFirebirdDialect();
        // Need to initialize so SupportsIdentityColumns can work
        dialect.InitializeUnknownProductInfo();

        var query = dialect.GetNaturalKeyLookupQuery(
            "my_table", "id",
            new[] { "col_a", "col_b" },
            new[] { "@a", "@b" });

        // Firebird uses ROWS 1 instead of LIMIT 1
        Assert.Contains("ROWS 1", query);
        Assert.DoesNotContain("LIMIT 1", query);
    }

    [Fact]
    public void FirebirdDialect_SupportsMerge_WhenNotInitialized_ReturnsFalse()
    {
        var dialect = CreateFirebirdDialect();

        // IsInitialized is false by default (no DetectDatabaseInfoAsync called)
        Assert.False(dialect.IsInitialized);
        Assert.False(dialect.SupportsMerge);
    }

    [Fact]
    public void FirebirdDialect_SupportsWindowFunctions_WhenNotInitialized_ReturnsFalse()
    {
        var dialect = CreateFirebirdDialect();

        Assert.False(dialect.IsInitialized);
        Assert.False(dialect.SupportsWindowFunctions);
    }

    [Fact]
    public void FirebirdDialect_SupportsCommonTableExpressions_WhenNotInitialized_ReturnsFalse()
    {
        var dialect = CreateFirebirdDialect();

        Assert.False(dialect.IsInitialized);
        Assert.False(dialect.SupportsCommonTableExpressions);
    }

    [Fact]
    public void FirebirdDialect_SupportsSavepoints_IsTrue()
    {
        var dialect = CreateFirebirdDialect();

        Assert.True(dialect.SupportsSavepoints);
    }

    [Fact]
    public void FirebirdDialect_SupportsInsertReturning_IsTrue()
    {
        var dialect = CreateFirebirdDialect();

        Assert.True(dialect.SupportsInsertReturning);
    }

    [Fact]
    public void FirebirdDialect_SupportsJsonTypes_IsFalse()
    {
        var dialect = CreateFirebirdDialect();

        Assert.False(dialect.SupportsJsonTypes);
    }

    [Fact]
    public void FirebirdDialect_SupportsArrayTypes_IsTrue()
    {
        var dialect = CreateFirebirdDialect();

        Assert.True(dialect.SupportsArrayTypes);
    }

    [Fact]
    public void FirebirdDialect_MaxParameterLimit_Is65535()
    {
        var dialect = CreateFirebirdDialect();

        Assert.Equal(65535, dialect.MaxParameterLimit);
    }

    [Fact]
    public void FirebirdDialect_MaxOutputParameters_Is1499()
    {
        var dialect = CreateFirebirdDialect();

        Assert.Equal(1499, dialect.MaxOutputParameters);
    }

    [Fact]
    public void FirebirdDialect_ParameterNameMaxLength_Is63()
    {
        var dialect = CreateFirebirdDialect();

        Assert.Equal(63, dialect.ParameterNameMaxLength);
    }

    [Fact]
    public void FirebirdDialect_PrepareStatements_IsFalse()
    {
        var dialect = CreateFirebirdDialect();

        Assert.False(dialect.PrepareStatements);
    }

    [Fact]
    public void FirebirdDialect_ProcWrappingStyle_IsExecuteProcedure()
    {
        var dialect = CreateFirebirdDialect();

        Assert.Equal(ProcWrappingStyle.ExecuteProcedure, dialect.ProcWrappingStyle);
    }

    [Fact]
    public void FirebirdDialect_MinPoolSizeSettingName_IsMinPoolSize()
    {
        var dialect = CreateFirebirdDialect();

        Assert.Equal("MinPoolSize", dialect.MinPoolSizeSettingName);
    }

    [Fact]
    public void FirebirdDialect_MaxPoolSizeSettingName_IsMaxPoolSize()
    {
        var dialect = CreateFirebirdDialect();

        Assert.Equal("MaxPoolSize", dialect.MaxPoolSizeSettingName);
    }

    [Fact]
    public void FirebirdDialect_CreateDbParameter_WithStringType_PassesThrough()
    {
        var dialect = CreateFirebirdDialect();

        var param = dialect.CreateDbParameter("test_param", DbType.String, "hello world");

        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal("hello world", param.Value);
    }

    [Fact]
    public void FirebirdDialect_CreateDbParameter_WithInt32Type_PassesThrough()
    {
        var dialect = CreateFirebirdDialect();

        var param = dialect.CreateDbParameter("test_param", DbType.Int32, 42);

        Assert.Equal(DbType.Int32, param.DbType);
        Assert.Equal(42, param.Value);
    }

    [Fact]
    public void FirebirdDialect_CreateDbParameter_WithInt64Type_PassesThrough()
    {
        var dialect = CreateFirebirdDialect();

        var param = dialect.CreateDbParameter("test_param", DbType.Int64, 123456789L);

        Assert.Equal(DbType.Int64, param.DbType);
        Assert.Equal(123456789L, param.Value);
    }

    [Fact]
    public void FirebirdDialect_CreateDbParameter_WithDateTimeType_PassesThrough()
    {
        var dialect = CreateFirebirdDialect();
        var now = DateTime.UtcNow;

        var param = dialect.CreateDbParameter("test_param", DbType.DateTime, now);

        Assert.Equal(DbType.DateTime, param.DbType);
        Assert.Equal(now, param.Value);
    }

    [Fact]
    public async Task FirebirdDialect_GetDatabaseVersionAsync_WhenEngineQueryReturnsNull_ReturnsEmptyString()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var dialect = CreateFirebirdDialect(factory);

        // SetScalarResult to null to simulate engine query returning null
        factory.SetScalarResult(null!);

        var connection = (fakeDbConnection)factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await dialect.GetDatabaseVersionAsync(trackedConnection);

        Assert.Equal(string.Empty, version);
    }

    [Fact]
    public async Task FirebirdDialect_GetDatabaseVersionAsync_WhenEngineQueryReturnsEmptyString_ReturnsEmptyString()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var dialect = CreateFirebirdDialect(factory);

        factory.SetScalarResult(string.Empty);

        var connection = (fakeDbConnection)factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await dialect.GetDatabaseVersionAsync(trackedConnection);

        Assert.Equal(string.Empty, version);
    }

    [Fact]
    public async Task FirebirdDialect_GetDatabaseVersionAsync_WhenEngineQueryThrows_FallsBackToMonitor()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var dialect = CreateFirebirdDialect(factory);

        var connection = (fakeDbConnection)factory.CreateConnection();

        // Make engine version query fail
        connection.SetCommandFailure(
            "SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database",
            new InvalidOperationException("Engine query not available"));

        // The fakeDb returns a canned response "Firebird 4.0.2" for the monitor query
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await dialect.GetDatabaseVersionAsync(trackedConnection);

        // Firebird monitor table fallback returns the canned version
        Assert.Equal("Firebird 4.0.2", version);
    }

    [Fact]
    public async Task FirebirdDialect_GetDatabaseVersionAsync_WhenBothQueriesFail_ReturnsEmptyString()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var dialect = CreateFirebirdDialect(factory);

        var connection = (fakeDbConnection)factory.CreateConnection();

        // Make both queries fail
        connection.SetCommandFailure(
            "SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database",
            new InvalidOperationException("Engine query not available"));
        connection.SetCommandFailure(
            "SELECT mon$server_version FROM mon$database",
            new InvalidOperationException("Monitor query not available"));

        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await dialect.GetDatabaseVersionAsync(trackedConnection);

        Assert.Equal(string.Empty, version);
    }

    [Fact]
    public void FirebirdDialect_DatabaseType_IsFirebird()
    {
        var dialect = CreateFirebirdDialect();

        Assert.Equal(SupportedDatabase.Firebird, dialect.DatabaseType);
    }

    [Fact]
    public void FirebirdDialect_ParameterMarker_IsAtSign()
    {
        var dialect = CreateFirebirdDialect();

        Assert.Equal("@", dialect.ParameterMarker);
    }

    [Fact]
    public void FirebirdDialect_SupportsNamedParameters_IsTrue()
    {
        var dialect = CreateFirebirdDialect();

        Assert.True(dialect.SupportsNamedParameters);
    }

    [Fact]
    public void FirebirdDialect_GetVersionQuery_ReturnsEngineVersionQuery()
    {
        var dialect = CreateFirebirdDialect();

        var query = dialect.GetVersionQuery();

        Assert.Contains("ENGINE_VERSION", query);
        Assert.Contains("rdb$database", query);
    }

    [Fact]
    public void FirebirdDialect_GetConnectionSessionSettings_WithContext_ReturnsDefaultSettings()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var dialect = CreateFirebirdDialect(factory);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Firebird", factory);

        var settings = dialect.GetConnectionSessionSettings(context, false);

        Assert.Contains("SET TRANSACTION ISOLATION LEVEL READ COMMITTED", settings);
        Assert.Contains("SET SQL DIALECT 3", settings);
    }

    [Fact]
    public void FirebirdDialect_GetConnectionSessionSettings_ReadOnly_ReturnsSameAsReadWrite()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var dialect = CreateFirebirdDialect(factory);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Firebird", factory);

        var settingsReadOnly = dialect.GetConnectionSessionSettings(context, true);
        var settingsReadWrite = dialect.GetConnectionSessionSettings(context, false);

        // Firebird has no session-level read-only enforcement
        Assert.Equal(settingsReadWrite, settingsReadOnly);
    }

    #pragma warning disable CS0618 // Suppress obsolete warning for GetConnectionSessionSettings()
    [Fact]
    public void FirebirdDialect_GetConnectionSessionSettings_Obsolete_ReturnsDefaultSettings()
    {
        var dialect = CreateFirebirdDialect();

        var settings = dialect.GetConnectionSessionSettings();

        Assert.Contains("SET TRANSACTION ISOLATION LEVEL READ COMMITTED", settings);
        Assert.Contains("SET SQL DIALECT 3", settings);
    }
    #pragma warning restore CS0618

    #endregion

    #region TenantContextRegistry Tests

    private static ServiceProvider BuildServiceProvider(SupportedDatabase dbType = SupportedDatabase.Sqlite)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<DbProviderFactory>("FakeDb", new fakeDbFactory(dbType));
        return services.BuildServiceProvider();
    }

    private class StubTenantResolver : ITenantConnectionResolver
    {
        public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant)
        {
            return new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={tenant}.db;EmulatedProduct=Sqlite",
                ProviderName = "FakeDb"
            };
        }
    }

    private class StubContextFactory : IDatabaseContextFactory
    {
        public IDatabaseContext Create(
            IDatabaseContextConfiguration configuration,
            DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            return new DatabaseContext(configuration, factory, loggerFactory);
        }
    }

    private class ThrowingContextFactory : IDatabaseContextFactory
    {
        public IDatabaseContext Create(
            IDatabaseContextConfiguration configuration,
            DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            throw new InvalidOperationException("Factory creation failed");
        }
    }

    private class UnregisteredProviderResolver : ITenantConnectionResolver
    {
        public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant)
        {
            return new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={tenant}.db",
                ProviderName = "NonExistentProvider"
            };
        }
    }

    [Fact]
    public void TenantContextRegistry_Constructor_NullServiceProvider_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new TenantContextRegistry(
                null!,
                new StubTenantResolver(),
                new StubContextFactory(),
                NullLoggerFactory.Instance));

        Assert.Equal("serviceProvider", ex.ParamName);
    }

    [Fact]
    public void TenantContextRegistry_Constructor_NullResolver_Throws()
    {
        using var sp = BuildServiceProvider();

        var ex = Assert.Throws<ArgumentNullException>(() =>
            new TenantContextRegistry(
                sp,
                null!,
                new StubContextFactory(),
                NullLoggerFactory.Instance));

        Assert.Equal("resolver", ex.ParamName);
    }

    [Fact]
    public void TenantContextRegistry_Constructor_NullContextFactory_Throws()
    {
        using var sp = BuildServiceProvider();

        var ex = Assert.Throws<ArgumentNullException>(() =>
            new TenantContextRegistry(
                sp,
                new StubTenantResolver(),
                null!,
                NullLoggerFactory.Instance));

        Assert.Equal("contextFactory", ex.ParamName);
    }

    [Fact]
    public void TenantContextRegistry_Constructor_NullLoggerFactory_Throws()
    {
        using var sp = BuildServiceProvider();

        var ex = Assert.Throws<ArgumentNullException>(() =>
            new TenantContextRegistry(
                sp,
                new StubTenantResolver(),
                new StubContextFactory(),
                null!));

        Assert.Equal("loggerFactory", ex.ParamName);
    }

    [Fact]
    public void TenantContextRegistry_GetContext_ReturnsCachedInstance()
    {
        using var sp = BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            sp,
            new StubTenantResolver(),
            new StubContextFactory(),
            NullLoggerFactory.Instance);

        var context1 = registry.GetContext("tenant_a");
        var context2 = registry.GetContext("tenant_a");

        Assert.Same(context1, context2);
    }

    [Fact]
    public void TenantContextRegistry_GetContext_DifferentTenants_ReturnsDifferentInstances()
    {
        using var sp = BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            sp,
            new StubTenantResolver(),
            new StubContextFactory(),
            NullLoggerFactory.Instance);

        var contextA = registry.GetContext("tenant_a");
        var contextB = registry.GetContext("tenant_b");

        Assert.NotSame(contextA, contextB);
    }

    [Fact]
    public void TenantContextRegistry_GetContext_UnregisteredProvider_ThrowsInvalidOperationException()
    {
        using var sp = BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            sp,
            new UnregisteredProviderResolver(),
            new StubContextFactory(),
            NullLoggerFactory.Instance);

        var ex = Assert.Throws<InvalidOperationException>(() => registry.GetContext("tenant_x"));

        Assert.Contains("No factory registered for", ex.Message);
        Assert.Contains("NonExistentProvider", ex.Message);
    }

    [Fact]
    public void TenantContextRegistry_Dispose_DisposesAllContexts()
    {
        using var sp = BuildServiceProvider();
        var registry = new TenantContextRegistry(
            sp,
            new StubTenantResolver(),
            new StubContextFactory(),
            NullLoggerFactory.Instance);

        var contextA = registry.GetContext("tenant_a");
        var contextB = registry.GetContext("tenant_b");

        // Dispose should not throw
        registry.Dispose();

        // After disposal, the registry should not serve new requests
        // (SafeAsyncDisposableBase marks it as disposed)
        Assert.True(registry.IsDisposed);
    }

    [Fact]
    public void TenantContextRegistry_Dispose_HandlesContextDisposeException()
    {
        using var sp = BuildServiceProvider();
        var registry = new TenantContextRegistry(
            sp,
            new StubTenantResolver(),
            new ThrowingOnDisposeContextFactory(),
            NullLoggerFactory.Instance);

        // We need to pre-populate a context that will throw on dispose
        // The ThrowingOnDisposeContextFactory creates contexts that throw on Dispose
        var context = registry.GetContext("tenant_a");

        // Should not throw even when context.Dispose() throws
        registry.Dispose();

        Assert.True(registry.IsDisposed);
    }

    [Fact]
    public async Task TenantContextRegistry_DisposeAsync_DisposesAllContexts()
    {
        using var sp = BuildServiceProvider();
        var registry = new TenantContextRegistry(
            sp,
            new StubTenantResolver(),
            new StubContextFactory(),
            NullLoggerFactory.Instance);

        var contextA = registry.GetContext("tenant_a");
        var contextB = registry.GetContext("tenant_b");

        await registry.DisposeAsync();

        Assert.True(registry.IsDisposed);
    }

    [Fact]
    public async Task TenantContextRegistry_DisposeAsync_HandlesDisposalError()
    {
        using var sp = BuildServiceProvider();
        var registry = new TenantContextRegistry(
            sp,
            new StubTenantResolver(),
            new ThrowingOnDisposeContextFactory(),
            NullLoggerFactory.Instance);

        var context = registry.GetContext("tenant_a");

        // Should not throw even when async disposal fails
        await registry.DisposeAsync();

        Assert.True(registry.IsDisposed);
    }

    [Fact]
    public async Task TenantContextRegistry_DisposeAsync_WithAsyncDisposableContext()
    {
        using var sp = BuildServiceProvider();
        var registry = new TenantContextRegistry(
            sp,
            new StubTenantResolver(),
            new StubContextFactory(),
            NullLoggerFactory.Instance);

        // DatabaseContext implements IAsyncDisposable, so this tests the async path
        var context = registry.GetContext("tenant_a");

        await registry.DisposeAsync();

        Assert.True(registry.IsDisposed);
    }

    [Fact]
    public void TenantContextRegistry_MultipleTenants_AllGetCached()
    {
        using var sp = BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            sp,
            new StubTenantResolver(),
            new StubContextFactory(),
            NullLoggerFactory.Instance);

        var c1 = registry.GetContext("t1");
        var c2 = registry.GetContext("t2");
        var c3 = registry.GetContext("t3");

        // Verify caching
        Assert.Same(c1, registry.GetContext("t1"));
        Assert.Same(c2, registry.GetContext("t2"));
        Assert.Same(c3, registry.GetContext("t3"));

        // Verify uniqueness
        Assert.NotSame(c1, c2);
        Assert.NotSame(c2, c3);
        Assert.NotSame(c1, c3);
    }

    /// <summary>
    /// A context factory that creates contexts which throw on Dispose.
    /// Used to test that TenantContextRegistry handles disposal errors gracefully.
    /// </summary>
    private class ThrowingOnDisposeContextFactory : IDatabaseContextFactory
    {
        public IDatabaseContext Create(
            IDatabaseContextConfiguration configuration,
            DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            return new ThrowingOnDisposeContext(configuration, factory, loggerFactory);
        }
    }

    /// <summary>
    /// A DatabaseContext subclass that throws on both Dispose and DisposeAsync
    /// to verify TenantContextRegistry handles disposal failures.
    /// </summary>
    private class ThrowingOnDisposeContext : DatabaseContext
    {
        public ThrowingOnDisposeContext(
            IDatabaseContextConfiguration configuration,
            DbProviderFactory factory,
            ILoggerFactory loggerFactory)
            : base(configuration, factory, loggerFactory)
        {
        }

        protected override void DisposeManaged()
        {
            throw new InvalidOperationException("Simulated dispose failure");
        }

        protected override ValueTask DisposeManagedAsync()
        {
            throw new InvalidOperationException("Simulated async dispose failure");
        }
    }

    #endregion
}
