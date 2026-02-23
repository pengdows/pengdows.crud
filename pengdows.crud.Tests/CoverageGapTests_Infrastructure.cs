using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for DefaultDatabaseContextFactory, InternalConnectionExtensions,
/// DatabaseContextTypeMapExtensions, and DatabaseContextConfiguration coverage gaps.
/// </summary>
public class CoverageGapTests_Infrastructure
{
    #region DefaultDatabaseContextFactory Tests

    [Fact]
    public void DefaultDatabaseContextFactory_NullConfiguration_ThrowsArgumentNullException()
    {
        var factory = new DefaultDatabaseContextFactory();
        var dbFactory = new fakeDbFactory(SupportedDatabase.Sqlite);

        var ex = Assert.Throws<ArgumentNullException>(() => factory.Create(null!, dbFactory, null!));
        Assert.Equal("configuration", ex.ParamName);
    }

    [Fact]
    public void DefaultDatabaseContextFactory_NullFactory_ThrowsArgumentNullException()
    {
        var sut = new DefaultDatabaseContextFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;"
        };

        var ex = Assert.Throws<ArgumentNullException>(() => sut.Create(config, null!, null!));
        Assert.Equal("factory", ex.ParamName);
    }

    [Fact]
    public void DefaultDatabaseContextFactory_NullLoggerFactory_Succeeds()
    {
        var sut = new DefaultDatabaseContextFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;"
        };
        var dbFactory = new fakeDbFactory(SupportedDatabase.Sqlite);

        using var context = factory_Create_And_Cast(sut, config, dbFactory);
        Assert.NotNull(context);
    }

    [Fact]
    public void DefaultDatabaseContextFactory_ValidInputs_ReturnsDatabaseContext()
    {
        var sut = new DefaultDatabaseContextFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;"
        };
        var dbFactory = new fakeDbFactory(SupportedDatabase.Sqlite);

        using var context = factory_Create_And_Cast(sut, config, dbFactory);
        Assert.NotNull(context);
        Assert.IsAssignableFrom<IDatabaseContext>(context);
    }

    [Fact]
    public void DefaultDatabaseContextFactory_ImplementsIDatabaseContextFactory()
    {
        var sut = new DefaultDatabaseContextFactory();
        Assert.IsAssignableFrom<IDatabaseContextFactory>(sut);
    }

    private static DatabaseContext factory_Create_And_Cast(
        DefaultDatabaseContextFactory sut,
        DatabaseContextConfiguration config,
        fakeDbFactory dbFactory)
    {
        var result = sut.Create(config, dbFactory, null!);
        return (DatabaseContext)result;
    }

    #endregion

    #region InternalConnectionExtensions Tests

    /// <summary>
    /// Stub that implements IDatabaseContext but NOT IInternalConnectionProvider.
    /// Used to test the extension method's guard clause.
    /// </summary>
    private sealed class NonProviderDatabaseContextStub : IDatabaseContext
    {
        public DbMode ConnectionMode => DbMode.Standard;
        public Guid RootId => Guid.Empty;
        public ReadWriteMode ReadWriteMode => ReadWriteMode.ReadWrite;
        public string ConnectionString => string.Empty;
        public string Name { get; set; } = string.Empty;
        public DbDataSource? DataSource => null;
        public IDataSourceInformation DataSourceInfo => throw new NotImplementedException();
        public ISqlDialect Dialect => throw new NotImplementedException();
        public TimeSpan? ModeLockTimeout => null;
        public string SessionSettingsPreamble => string.Empty;
        public ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Call;
        public int MaxParameterLimit => 2100;
        public int MaxOutputParameters => 2100;
        public long NumberOfOpenConnections => 0;
        public DatabaseMetrics Metrics => throw new NotImplementedException();
        public event EventHandler<DatabaseMetrics> MetricsUpdated { add { } remove { } }
        public SupportedDatabase Product => SupportedDatabase.Sqlite;
        public long PeakOpenConnections => 0;
        public bool? ForceManualPrepare => null;
        public bool? DisablePrepare => null;
        public bool IsReadOnlyConnection => false;
        public bool RCSIEnabled => false;
        public bool SnapshotIsolationEnabled => false;
        public bool SupportsInsertReturning => false;
        public string QuotePrefix => "\"";
        public string QuoteSuffix => "\"";
        public string CompositeIdentifierSeparator => ".";
        public bool IsDisposed => false;

        public ILockerAsync GetLock() => throw new NotImplementedException();
        public ISqlContainer CreateSqlContainer(string? query = null) => throw new NotImplementedException();
        public DbParameter CreateDbParameter<T>(string? name, DbType type, T value) => throw new NotImplementedException();
        public DbParameter CreateDbParameter<T>(string? name, DbType type, T value, ParameterDirection direction) => throw new NotImplementedException();
        public DbParameter CreateDbParameter<T>(DbType type, T value) => throw new NotImplementedException();
        public string WrapObjectName(string name) => throw new NotImplementedException();
        public string MakeParameterName(DbParameter dbParameter) => throw new NotImplementedException();
        public string MakeParameterName(string parameterName) => throw new NotImplementedException();
        public ITransactionContext BeginTransaction(IsolationLevel? isolationLevel = null, ExecutionType executionType = ExecutionType.Write, bool? readOnly = null) => throw new NotImplementedException();
        public ITransactionContext BeginTransaction(IsolationProfile isolationProfile, ExecutionType executionType = ExecutionType.Write, bool? readOnly = null) => throw new NotImplementedException();
        public Task<ITransactionContext> BeginTransactionAsync(IsolationLevel? isolationLevel = null, ExecutionType executionType = ExecutionType.Write, bool? readOnly = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ITransactionContext> BeginTransactionAsync(IsolationProfile isolationProfile, ExecutionType executionType = ExecutionType.Write, bool? readOnly = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30) => throw new NotImplementedException();
        public void AssertIsWriteConnection() => throw new NotImplementedException();
        public void AssertIsReadConnection() => throw new NotImplementedException();
        public void CloseAndDisposeConnection(ITrackedConnection? conn) { }
        public ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? conn) => default;
        public void Dispose() { }
        public ValueTask DisposeAsync() => default;
    }

    [Fact]
    public void InternalConnectionExtensions_NonProvider_ThrowsInvalidOperationException()
    {
        IDatabaseContext stub = new NonProviderDatabaseContextStub();

        var ex = Assert.Throws<InvalidOperationException>(
            () => InternalConnectionExtensions.GetConnection(stub, ExecutionType.Read));

        Assert.Contains("IDatabaseContext must provide internal connection access", ex.Message);
    }

    [Fact]
    public void InternalConnectionExtensions_RealDatabaseContext_ReturnsConnection()
    {
        var dbFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=test;", dbFactory);

        // DatabaseContext implements IInternalConnectionProvider, so this should work
        var connection = InternalConnectionExtensions.GetConnection(context, ExecutionType.Read);
        Assert.NotNull(connection);

        context.CloseAndDisposeConnection(connection);
    }

    [Fact]
    public void InternalConnectionExtensions_WriteExecution_ReturnsConnection()
    {
        var dbFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=test;", dbFactory);

        var connection = InternalConnectionExtensions.GetConnection(context, ExecutionType.Write);
        Assert.NotNull(connection);

        context.CloseAndDisposeConnection(connection);
    }

    [Fact]
    public void InternalConnectionExtensions_SharedFlag_ReturnsConnection()
    {
        var dbFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=test;", dbFactory);

        var connection = InternalConnectionExtensions.GetConnection(context, ExecutionType.Read, isShared: true);
        Assert.NotNull(connection);

        context.CloseAndDisposeConnection(connection);
    }

    #endregion

    #region DatabaseContextTypeMapExtensions Tests

    [Fact]
    public void GetInternalTypeMapRegistry_NonAccessor_ThrowsInvalidOperationException()
    {
        IDatabaseContext stub = new NonProviderDatabaseContextStub();

        var ex = Assert.Throws<InvalidOperationException>(
            () => DatabaseContextTypeMapExtensions.GetInternalTypeMapRegistry(stub));

        Assert.Contains("IDatabaseContext does not expose an internal TypeMapRegistry", ex.Message);
    }

    [Fact]
    public void GetInternalTypeMapRegistry_RealContext_ReturnsRegistry()
    {
        var dbFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=test;", dbFactory);

        var registry = DatabaseContextTypeMapExtensions.GetInternalTypeMapRegistry(context);
        Assert.NotNull(registry);
        Assert.IsAssignableFrom<ITypeMapRegistry>(registry);
    }

    [Fact]
    public void RegisterEntity_NonAccessor_ThrowsInvalidOperationException()
    {
        IDatabaseContext stub = new NonProviderDatabaseContextStub();

        Assert.Throws<InvalidOperationException>(
            () => DatabaseContextTypeMapExtensions.RegisterEntity<SimpleTestEntity>(stub));
    }

    [Fact]
    public void RegisterEntity_RealContext_RegistersSuccessfully()
    {
        var dbFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=test;", dbFactory);

        // Should not throw
        DatabaseContextTypeMapExtensions.RegisterEntity<SimpleTestEntity>(context);

        // Verify registration by getting the type map
        var registry = DatabaseContextTypeMapExtensions.GetInternalTypeMapRegistry(context);
        var tableInfo = registry.GetTableInfo<SimpleTestEntity>();
        Assert.NotNull(tableInfo);
    }

    #endregion

    #region DatabaseContextConfiguration Tests

    [Fact]
    public void Configuration_Defaults_AreCorrect()
    {
        var config = new DatabaseContextConfiguration();

        Assert.Equal(string.Empty, config.ConnectionString);
        Assert.Equal(string.Empty, config.ReadOnlyConnectionString);
        Assert.Equal(string.Empty, config.ProviderName);
        Assert.Equal(DbMode.Best, config.DbMode);
        Assert.Equal(ReadWriteMode.ReadWrite, config.ReadWriteMode);
        Assert.Null(config.ForceManualPrepare);
        Assert.Null(config.DisablePrepare);
        Assert.False(config.EnableMetrics);
        Assert.NotNull(config.MetricsOptions);
        Assert.Null(config.MaxConcurrentWrites);
        Assert.Null(config.MaxConcurrentReads);
        Assert.True(config.EnableSingleWriterFairness);
        Assert.Equal(TimeSpan.FromSeconds(DatabaseContextConfiguration.DefaultPoolAcquireSeconds), config.PoolAcquireTimeout);
        Assert.Equal(TimeSpan.FromSeconds(DatabaseContextConfiguration.DefaultModeLockSeconds), config.ModeLockTimeout);
        Assert.Equal(string.Empty, config.ApplicationName);
    }

    [Fact]
    public void Configuration_ReadWriteMode_WriteOnlyConvertsToReadWrite()
    {
        var config = new DatabaseContextConfiguration
        {
            ReadWriteMode = ReadWriteMode.WriteOnly
        };

        Assert.Equal(ReadWriteMode.ReadWrite, config.ReadWriteMode);
    }

    [Fact]
    public void Configuration_ReadWriteMode_ReadOnlyStaysReadOnly()
    {
        var config = new DatabaseContextConfiguration
        {
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        Assert.Equal(ReadWriteMode.ReadOnly, config.ReadWriteMode);
    }

    [Fact]
    public void Configuration_ReadWriteMode_ReadWriteStaysReadWrite()
    {
        var config = new DatabaseContextConfiguration
        {
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        Assert.Equal(ReadWriteMode.ReadWrite, config.ReadWriteMode);
    }

    [Fact]
    public void Configuration_MaxConcurrentWrites_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        config.MaxConcurrentWrites = 10;
        Assert.Equal(10, config.MaxConcurrentWrites);

        config.MaxConcurrentWrites = null;
        Assert.Null(config.MaxConcurrentWrites);
    }

    [Fact]
    public void Configuration_MaxConcurrentReads_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        config.MaxConcurrentReads = 20;
        Assert.Equal(20, config.MaxConcurrentReads);

        config.MaxConcurrentReads = null;
        Assert.Null(config.MaxConcurrentReads);
    }

    [Fact]
    public void Configuration_PoolAcquireTimeout_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        var customTimeout = TimeSpan.FromSeconds(15);
        config.PoolAcquireTimeout = customTimeout;
        Assert.Equal(customTimeout, config.PoolAcquireTimeout);
    }

    [Fact]
    public void Configuration_ModeLockTimeout_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        var customTimeout = TimeSpan.FromSeconds(60);
        config.ModeLockTimeout = customTimeout;
        Assert.Equal(customTimeout, config.ModeLockTimeout);

        config.ModeLockTimeout = null;
        Assert.Null(config.ModeLockTimeout);
    }

    [Fact]
    public void Configuration_EnableSingleWriterFairness_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        Assert.True(config.EnableSingleWriterFairness);
        config.EnableSingleWriterFairness = false;
        Assert.False(config.EnableSingleWriterFairness);
    }

    [Fact]
    public void Configuration_ForceManualPrepare_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        Assert.Null(config.ForceManualPrepare);
        config.ForceManualPrepare = true;
        Assert.True(config.ForceManualPrepare);
        config.ForceManualPrepare = false;
        Assert.False(config.ForceManualPrepare);
    }

    [Fact]
    public void Configuration_DisablePrepare_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        Assert.Null(config.DisablePrepare);
        config.DisablePrepare = true;
        Assert.True(config.DisablePrepare);
        config.DisablePrepare = false;
        Assert.False(config.DisablePrepare);
    }

    [Fact]
    public void Configuration_EnableMetrics_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        Assert.False(config.EnableMetrics);
        config.EnableMetrics = true;
        Assert.True(config.EnableMetrics);
    }

    [Fact]
    public void Configuration_MetricsOptions_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        var custom = new MetricsOptions { EnableApproxPercentiles = true };
        config.MetricsOptions = custom;
        Assert.Same(custom, config.MetricsOptions);
    }

    [Fact]
    public void Configuration_ApplicationName_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        Assert.Equal(string.Empty, config.ApplicationName);
        config.ApplicationName = "MyApp";
        Assert.Equal("MyApp", config.ApplicationName);
    }

    [Fact]
    public void Configuration_ConnectionString_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        config.ConnectionString = "Server=localhost;Database=test;";
        Assert.Equal("Server=localhost;Database=test;", config.ConnectionString);
    }

    [Fact]
    public void Configuration_ReadOnlyConnectionString_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        config.ReadOnlyConnectionString = "Server=replica;Database=test;";
        Assert.Equal("Server=replica;Database=test;", config.ReadOnlyConnectionString);
    }

    [Fact]
    public void Configuration_ProviderName_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        config.ProviderName = "Npgsql";
        Assert.Equal("Npgsql", config.ProviderName);
    }

    [Fact]
    public void Configuration_DbMode_GetSet()
    {
        var config = new DatabaseContextConfiguration();

        config.DbMode = DbMode.SingleConnection;
        Assert.Equal(DbMode.SingleConnection, config.DbMode);

        config.DbMode = DbMode.KeepAlive;
        Assert.Equal(DbMode.KeepAlive, config.DbMode);

        config.DbMode = DbMode.SingleWriter;
        Assert.Equal(DbMode.SingleWriter, config.DbMode);

        config.DbMode = DbMode.Standard;
        Assert.Equal(DbMode.Standard, config.DbMode);
    }

    [Fact]
    public void Configuration_DefaultConstants_AreExpected()
    {
        Assert.Equal(5, DatabaseContextConfiguration.DefaultPoolAcquireSeconds);
        Assert.Equal(30, DatabaseContextConfiguration.DefaultModeLockSeconds);
    }

    #endregion

    #region DatabaseContext Config Validation

    [Fact]
    public void DatabaseContext_ZeroPoolAcquireTimeout_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            PoolAcquireTimeout = TimeSpan.Zero
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new DatabaseContext(config, factory));
    }

    [Fact]
    public void DatabaseContext_NegativePoolAcquireTimeout_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            PoolAcquireTimeout = TimeSpan.FromSeconds(-1)
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new DatabaseContext(config, factory));
    }

    [Fact]
    public void DatabaseContext_ZeroMaxConcurrentReads_Throws()
    {
        // Validation now happens at the setter (fail-fast) rather than at DatabaseContext construction.
        var config = new DatabaseContextConfiguration();
        Assert.Throws<ArgumentOutOfRangeException>(() => config.MaxConcurrentReads = 0);
    }

    [Fact]
    public void DatabaseContext_NegativeMaxConcurrentWrites_Throws()
    {
        // Validation now happens at the setter (fail-fast) rather than at DatabaseContext construction.
        var config = new DatabaseContextConfiguration();
        Assert.Throws<ArgumentOutOfRangeException>(() => config.MaxConcurrentWrites = -1);
    }

    [Fact]
    public void DatabaseContext_ZeroModeLockTimeout_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            ModeLockTimeout = TimeSpan.Zero
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new DatabaseContext(config, factory));
    }

    [Fact]
    public void DatabaseContext_NullModeLockTimeout_DoesNotThrow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=:memory:",
                ModeLockTimeout = null
            },
            factory);
        Assert.NotNull(ctx);
    }

    [Fact]
    public void DatabaseContext_ValidGovernorLimits_DoesNotThrow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=:memory:",
                MaxConcurrentReads = 10,
                MaxConcurrentWrites = 5
            },
            factory);
        Assert.NotNull(ctx);
    }

    #endregion

    #region Test Entities

    [pengdows.crud.attributes.Table("simple_test")]
    private class SimpleTestEntity
    {
        [pengdows.crud.attributes.Id]
        [pengdows.crud.attributes.Column("id", DbType.Int64)]
        public long Id { get; set; }

        [pengdows.crud.attributes.Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    #endregion
}
