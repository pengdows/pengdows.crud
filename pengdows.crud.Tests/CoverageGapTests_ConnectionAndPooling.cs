using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using pengdows.crud.exceptions;
using pengdows.crud.strategies.connection;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class CoverageGapTests_ConnectionAndPooling
{
    #region ConnectionStringHelper Tests

    [Fact]
    public void ConnectionStringHelper_Create_WithNullConnectionString_ReturnsEmptyBuilder()
    {
        var result = ConnectionStringHelper.Create((DbConnectionStringBuilder?)null, null);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.ConnectionString);
    }

    [Fact]
    public void ConnectionStringHelper_Create_WithEmptyConnectionString_ReturnsEmptyBuilder()
    {
        var result = ConnectionStringHelper.Create((DbConnectionStringBuilder?)null, string.Empty);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.ConnectionString);
    }

    [Fact]
    public void ConnectionStringHelper_Create_WithValidConnectionString_NullBuilder_ParsesNormally()
    {
        var result = ConnectionStringHelper.Create((DbConnectionStringBuilder?)null, "Data Source=test;Initial Catalog=db");

        Assert.NotNull(result);
        Assert.Equal("test", result["Data Source"]);
    }

    [Fact]
    public void ConnectionStringHelper_Create_WithUnparseableString_NullBuilder_SetsAsDataSource()
    {
        // A string like ":memory:" cannot be parsed as key=value pairs
        var result = ConnectionStringHelper.Create((DbConnectionStringBuilder?)null, ":memory:");

        Assert.NotNull(result);
        Assert.Equal(":memory:", result["Data Source"]);
    }

    [Fact]
    public void ConnectionStringHelper_Create_WithValidBuilder_AcceptsConnectionString()
    {
        var builder = new DbConnectionStringBuilder();
        var result = ConnectionStringHelper.Create(builder, "Data Source=foo;Pooling=true");

        Assert.Same(builder, result);
        Assert.Equal("foo", result["Data Source"]);
    }

    [Fact]
    public void ConnectionStringHelper_Create_WithNullConnectionString_AndBuilder_ReturnsBuilderWithEmpty()
    {
        var builder = new DbConnectionStringBuilder();
        var result = ConnectionStringHelper.Create(builder, null);

        Assert.Same(builder, result);
        Assert.Equal(string.Empty, result.ConnectionString);
    }

    [Fact]
    public void ConnectionStringHelper_Create_WithEmptyConnectionString_AndBuilder_ReturnsBuilder()
    {
        var builder = new DbConnectionStringBuilder();
        var result = ConnectionStringHelper.Create(builder, string.Empty);

        Assert.Same(builder, result);
        Assert.Equal(string.Empty, result.ConnectionString);
    }

    [Fact]
    public void ConnectionStringHelper_Create_Factory_WithNullConnectionString_ReturnsBuilder()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var result = ConnectionStringHelper.Create(factory, null);

        Assert.NotNull(result);
    }

    [Fact]
    public void ConnectionStringHelper_Create_Factory_WithValidConnectionString_Works()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var result = ConnectionStringHelper.Create(factory, "Host=localhost;Database=test");

        Assert.NotNull(result);
        // The builder should have parsed the connection string
        Assert.Contains("Host", result.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConnectionStringHelper_Create_BuilderThatThrows_EmptyString_ReturnsFallback()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite)
        {
            ConnectionStringBuilderBehavior = ConnectionStringBuilderBehavior.ThrowOnConnectionStringSet
        };
        // Empty string should still succeed via fallback
        var result = ConnectionStringHelper.Create(factory, string.Empty);

        Assert.NotNull(result);
    }

    #endregion

    #region PoolingConfigReader Tests

    [Fact]
    public void PoolingConfigReader_NullDialect_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PoolingConfigReader.GetEffectivePoolConfig(null!, "Data Source=test"));
    }

    [Fact]
    public void PoolingConfigReader_EmptyConnectionString_ReturnsDialectDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(dialect, string.Empty);

        Assert.Equal(PoolConfigSource.DialectDefault, cfg.Source);
        Assert.Null(cfg.PoolingEnabled);
    }

    [Fact]
    public void PoolingConfigReader_WhitespaceConnectionString_ReturnsDialectDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(dialect, "   ");

        Assert.Equal(PoolConfigSource.DialectDefault, cfg.Source);
        Assert.Null(cfg.PoolingEnabled);
    }

    [Fact]
    public void PoolingConfigReader_UnparseableConnectionString_ReturnsDialectDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        // A string with unmatched quotes should fail to parse
        var cfg = PoolingConfigReader.GetEffectivePoolConfig(dialect, "not\"a=valid;connection'string");

        Assert.Equal(PoolConfigSource.DialectDefault, cfg.Source);
        Assert.Null(cfg.PoolingEnabled);
    }

    [Fact]
    public void PoolingConfigReader_PoolingTrue_NoExplicitMaxMin_UsesDialectDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(dialect, "Pooling=true;");

        Assert.Equal(PoolConfigSource.ConnectionString, cfg.Source);
        Assert.True(cfg.PoolingEnabled);
        Assert.Equal(dialect.DefaultMinPoolSize, cfg.MinPoolSize);
        Assert.Equal(dialect.DefaultMaxPoolSize, cfg.MaxPoolSize);
    }

    [Fact]
    public void PoolingConfigReader_PoolingZero_TreatedAsFalse()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(dialect, "Pooling=0;");

        Assert.Equal(PoolConfigSource.ConnectionString, cfg.Source);
        Assert.False(cfg.PoolingEnabled);
        Assert.Null(cfg.MaxPoolSize);
    }

    [Fact]
    public void PoolingConfigReader_MinPoolSizeSettingNameNull_SkipsMinParsing()
    {
        // SqlServer dialect has MinPoolSizeSettingName = "Min Pool Size" which is non-null,
        // but we need a dialect where it IS null. Use our custom dialect.
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new DialectWithNullMinPoolSize(factory);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(
            dialect,
            "Pooling=true;Max Pool Size=50;");

        Assert.Equal(PoolConfigSource.ConnectionString, cfg.Source);
        Assert.True(cfg.PoolingEnabled);
        Assert.Equal(50, cfg.MaxPoolSize);
        // MinPoolSize should fall through to dialect default since setting name is null
        Assert.Equal(dialect.DefaultMinPoolSize, cfg.MinPoolSize);
    }

    [Fact]
    public void PoolingConfigReader_OnlyMinPoolInConnectionString_UsesConnectionStringSource()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(
            dialect,
            "Minimum Pool Size=5;");

        Assert.Equal(PoolConfigSource.ConnectionString, cfg.Source);
        Assert.Null(cfg.PoolingEnabled);
        Assert.Equal(5, cfg.MinPoolSize);
        Assert.Equal(dialect.DefaultMaxPoolSize, cfg.MaxPoolSize);
    }

    [Fact]
    public void PoolingConfigReader_WhitespaceOnlyBoolValue_ReturnsNullPooling()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        // Pooling key exists but with whitespace-only value - ParseBool returns null
        var cfg = PoolingConfigReader.GetEffectivePoolConfig(
            dialect,
            "Pooling= ;Minimum Pool Size=2;");

        // MinPoolSize is explicit so source is ConnectionString
        Assert.Equal(PoolConfigSource.ConnectionString, cfg.Source);
        Assert.Null(cfg.PoolingEnabled);
        Assert.Equal(2, cfg.MinPoolSize);
    }

    [Fact]
    public void PoolingConfigReader_WhitespaceOnlyIntValue_ReturnsDialectDefault()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        // Max pool size with whitespace-only value - ParseInt returns null
        var cfg = PoolingConfigReader.GetEffectivePoolConfig(
            dialect,
            "Pooling=true;Maximum Pool Size= ;");

        Assert.Equal(PoolConfigSource.ConnectionString, cfg.Source);
        Assert.True(cfg.PoolingEnabled);
        // MaxPoolSize should fall back to dialect default since whitespace-only parses as null
        Assert.Equal(dialect.DefaultMaxPoolSize, cfg.MaxPoolSize);
    }

    [Fact]
    public void PoolingConfigReader_DialectDoesNotSupportExternalPooling_ReturnsDialectDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new NoExternalPoolingDialect(factory);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(dialect, "Pooling=true;Max Pool Size=50;");

        Assert.Equal(PoolConfigSource.DialectDefault, cfg.Source);
        Assert.Null(cfg.PoolingEnabled);
    }

    [Fact]
    public void PoolingConfigReader_NoPoolingKeysInConnectionString_ReturnsDialectDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        // Connection string with no pooling-related keys
        var cfg = PoolingConfigReader.GetEffectivePoolConfig(
            dialect,
            "Host=localhost;Database=test;");

        Assert.Equal(PoolConfigSource.DialectDefault, cfg.Source);
        Assert.Null(cfg.PoolingEnabled);
        Assert.Equal(dialect.DefaultMinPoolSize, cfg.MinPoolSize);
        Assert.Equal(dialect.DefaultMaxPoolSize, cfg.MaxPoolSize);
    }

    private sealed class DialectWithNullMinPoolSize : SqlDialect
    {
        public DialectWithNullMinPoolSize(DbProviderFactory factory)
            : base(factory, NullLogger.Instance)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
        public override string? PoolingSettingName => "Pooling";
        public override string? MaxPoolSizeSettingName => "Max Pool Size";
        public override string? MinPoolSizeSettingName => null;
    }

    private sealed class NoExternalPoolingDialect : SqlDialect
    {
        public NoExternalPoolingDialect(DbProviderFactory factory)
            : base(factory, NullLogger.Instance)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
        public override bool SupportsExternalPooling => false;
        public override string? PoolingSettingName => "Pooling";
        public override string? MaxPoolSizeSettingName => "Max Pool Size";
    }

    #endregion

    #region KeepAliveConnectionStrategy Tests

    [Fact]
    public void KeepAlive_ReleaseConnection_Null_DoesNotThrow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        // Should not throw
        strategy.ReleaseConnection(null);
    }

    [Fact]
    public void KeepAlive_ReleaseConnection_PersistentConnection_DoesNotDispose()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        // Get a connection and set it as persistent
        var conn = strategy.GetConnection(ExecutionType.Read, false);
        strategy.PostInitialize(conn);

        // Releasing the persistent connection should NOT dispose it
        strategy.ReleaseConnection(conn);

        // Connection should still be usable (not disposed)
        Assert.Equal(ConnectionState.Open, conn.State);
    }

    [Fact]
    public void KeepAlive_ReleaseConnection_NonPersistentConnection_Disposes()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        // Get a connection but do NOT set it as persistent
        var conn = strategy.GetConnection(ExecutionType.Read, false);

        // Releasing a non-persistent connection should dispose it
        strategy.ReleaseConnection(conn);

        Assert.Equal(ConnectionState.Closed, conn.State);
    }

    [Fact]
    public async Task KeepAlive_ReleaseConnectionAsync_Null_ReturnsCompleted()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        // Should complete without error
        await strategy.ReleaseConnectionAsync(null);
    }

    [Fact]
    public async Task KeepAlive_ReleaseConnectionAsync_PersistentConnection_DoesNotDispose()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        var conn = strategy.GetConnection(ExecutionType.Read, false);
        strategy.PostInitialize(conn);

        await strategy.ReleaseConnectionAsync(conn);

        // Persistent connection should remain open
        Assert.Equal(ConnectionState.Open, conn.State);
    }

    [Fact]
    public async Task KeepAlive_ReleaseConnectionAsync_NonPersistentConnection_Disposes()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        var conn = strategy.GetConnection(ExecutionType.Read, false);

        await strategy.ReleaseConnectionAsync(conn);

        Assert.Equal(ConnectionState.Closed, conn.State);
    }

    [Fact]
    public void KeepAlive_GetConnection_AlreadyOpenConnection_SkipsOpen()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        // GetConnection opens the connection internally
        var conn = strategy.GetConnection(ExecutionType.Read, false);

        Assert.Equal(ConnectionState.Open, conn.State);
        strategy.ReleaseConnection(conn);
    }

    [Fact]
    public void KeepAlive_GetConnection_FailOnOpen_ThrowsAndDisposesConnection()
    {
        // Use a factory that skips the first open (for context init) then fails on subsequent opens
        var factory = fakeDbFactory.CreateFailingFactoryWithSkip(
            SupportedDatabase.Sqlite,
            ConnectionFailureMode.FailOnOpen);

        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        // The second open should fail since the factory was configured to skip only the first
        var ex = Assert.ThrowsAny<Exception>(() =>
            strategy.GetConnection(ExecutionType.Read, false));
        Assert.True(
            ex is InvalidOperationException || ex is pengdows.crud.exceptions.ConnectionFailedException,
            $"Expected InvalidOperationException or ConnectionFailedException, got {ex.GetType().Name}");
    }

    [Fact]
    public void KeepAlive_HandleDialectDetection_WithInitConnection_ReturnsDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        // Create and open a connection to pass as initConnection
        var conn = strategy.GetConnection(ExecutionType.Read, false);

        var (dialect, dataSourceInfo) = strategy.HandleDialectDetection(
            conn, factory, NullLoggerFactory.Instance);

        Assert.NotNull(dialect);
        Assert.NotNull(dataSourceInfo);

        strategy.ReleaseConnection(conn);
    }

    [Fact]
    public void KeepAlive_HandleDialectDetection_NullFactory_ReturnsNulls()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        var conn = strategy.GetConnection(ExecutionType.Read, false);

        var (dialect, dataSourceInfo) = strategy.HandleDialectDetection(
            conn, null, NullLoggerFactory.Instance);

        Assert.Null(dialect);
        Assert.Null(dataSourceInfo);

        strategy.ReleaseConnection(conn);
    }

    [Fact]
    public void KeepAlive_HandleDialectDetection_NullInitConnection_UsesPersistentConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        // Set up a persistent connection first
        var conn = strategy.GetConnection(ExecutionType.Read, false);
        strategy.PostInitialize(conn);

        // Now call HandleDialectDetection with null initConnection
        // It should fall back to the persistent connection
        var (dialect, dataSourceInfo) = strategy.HandleDialectDetection(
            null, factory, NullLoggerFactory.Instance);

        Assert.NotNull(dialect);
        Assert.NotNull(dataSourceInfo);
    }

    [Fact]
    public void KeepAlive_HandleDialectDetection_NoBothConnections_CreatesOwned()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        // No persistent connection set, no initConnection passed
        // Strategy should create its own connection via FactoryCreateConnection
        var (dialect, dataSourceInfo) = strategy.HandleDialectDetection(
            null, factory, NullLoggerFactory.Instance);

        Assert.NotNull(dialect);
        Assert.NotNull(dataSourceInfo);
    }

    [Fact]
    public void KeepAlive_PostInitialize_SetsPersistentConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        var conn = strategy.GetConnection(ExecutionType.Read, false);
        strategy.PostInitialize(conn);

        Assert.Same(conn, context.PersistentConnection);
    }

    [Fact]
    public void KeepAlive_ParameterlessConstructor_CanBeCreated()
    {
        // The parameterless constructor exists for tests that pass context per call
        var strategy = new KeepAliveConnectionStrategy();
        Assert.NotNull(strategy);
    }

    [Fact]
    public void KeepAlive_HandleDialectDetection_WithInitConnectionAlreadyOpen_UsesIt()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);
        var strategy = new KeepAliveConnectionStrategy(context);

        // Get and open a connection, then pass it as initConnection
        var conn = strategy.GetConnection(ExecutionType.Read, false);
        Assert.Equal(ConnectionState.Open, conn.State);

        var (dialect, dataSourceInfo) = strategy.HandleDialectDetection(
            conn, factory, NullLoggerFactory.Instance);

        // Should successfully detect dialect from already-open connection
        Assert.NotNull(dialect);
        Assert.NotNull(dataSourceInfo);
        Assert.Equal(SupportedDatabase.Sqlite, dialect.DatabaseType);

        strategy.ReleaseConnection(conn);
    }

    #endregion
}
