using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MySql.Data.MySqlClient;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolingConfigReaderTests
{
    [Fact]
    public void GetEffectivePoolConfig_PostgreSql_UsesConnectionStringValues()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        var cs =
            "Host=localhost;Database=postgres;Username=postgres;Password=x;Pooling=true;Minimum Pool Size=3;Maximum Pool Size=42;";
        var cfg = PoolingConfigReader.GetEffectivePoolConfig(dialect, cs);

        Assert.Equal(PoolConfigSource.ConnectionString, cfg.Source);
        Assert.True(cfg.PoolingEnabled);
        Assert.Equal(3, cfg.MinPoolSize);
        Assert.Equal(42, cfg.MaxPoolSize);
    }

    [Fact]
    public void GetEffectivePoolConfig_WhenDialectDoesNotSupportExternalPooling_ReturnsDialectDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var dialect = new DuckDbDialect(factory, NullLogger.Instance);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(dialect, "DataSource=:memory:");

        Assert.Equal(PoolConfigSource.DialectDefault, cfg.Source);
        Assert.Null(cfg.PoolingEnabled);
        Assert.Null(cfg.MinPoolSize);
        Assert.NotNull(cfg.MaxPoolSize);
    }

    [Fact]
    public void GetEffectivePoolConfig_WhenPoolingNumericFlag_ParsesBoolAndInts()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(
            dialect,
            "Pooling=1;Minimum Pool Size=0;Maximum Pool Size=5;");

        Assert.Equal(PoolConfigSource.ConnectionString, cfg.Source);
        Assert.True(cfg.PoolingEnabled);
        Assert.Equal(0, cfg.MinPoolSize);
        Assert.Equal(5, cfg.MaxPoolSize);
    }

    [Fact]
    public void GetEffectivePoolConfig_WhenBoolInvalid_UsesConnectionStringDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(
            dialect,
            "Pooling=maybe;Minimum Pool Size=3;");

        Assert.Equal(PoolConfigSource.ConnectionString, cfg.Source);
        Assert.Null(cfg.PoolingEnabled);
        Assert.Equal(3, cfg.MinPoolSize);
        Assert.Equal(dialect.DefaultMaxPoolSize, cfg.MaxPoolSize);
    }

    [Fact]
    public void GetEffectivePoolConfig_WhenMaxPoolInvalid_UsesDialectDefault()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(
            dialect,
            "Pooling=true;Maximum Pool Size=not-a-number;");

        Assert.Equal(PoolConfigSource.ConnectionString, cfg.Source);
        Assert.True(cfg.PoolingEnabled);
        Assert.Null(cfg.MinPoolSize);
        Assert.Equal(dialect.DefaultMaxPoolSize, cfg.MaxPoolSize);
    }

    [Fact]
    public void GetEffectivePoolConfig_WhenDialectMissingPoolingKeys_ReturnsDialectDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new PoolingDialectMissingKeys(factory);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(
            dialect,
            "Pooling=true;Max Pool Size=5;");

        Assert.Equal(PoolConfigSource.DialectDefault, cfg.Source);
        Assert.Null(cfg.PoolingEnabled);
    }

    [Fact]
    public void GetEffectivePoolConfig_MySqlDataReadOnlySplit_PreservesConfiguredMaxPoolSize()
    {
        var dialect = new MySqlDialect(MySqlClientFactory.Instance, NullLogger.Instance);
        const string writerConnectionString =
            "Server=localhost;Database=test;User Id=root;Password=pass;Pooling=true;Max Pool Size=128;";

        var readerConnectionString = dialect.GetReadOnlyConnectionString(writerConnectionString);

        var writerConfig = PoolingConfigReader.GetEffectivePoolConfig(dialect, writerConnectionString);
        var readerConfig = PoolingConfigReader.GetEffectivePoolConfig(dialect, readerConnectionString);

        Assert.Equal(PoolConfigSource.ConnectionString, writerConfig.Source);
        Assert.Equal(PoolConfigSource.ConnectionString, readerConfig.Source);
        Assert.Equal(128, writerConfig.MaxPoolSize);
        Assert.Equal(128, readerConfig.MaxPoolSize);
    }

    [Fact]
    public void ConnectionStringHelper_MySqlDataReadOnlySplit_ExposesConfiguredMaxPoolSizeKey()
    {
        var dialect = new MySqlDialect(MySqlClientFactory.Instance, NullLogger.Instance);
        const string writerConnectionString =
            "Server=localhost;Database=test;User Id=root;Password=pass;Pooling=true;Max Pool Size=128;";

        var readerConnectionString = dialect.GetReadOnlyConnectionString(writerConnectionString);
        var builder = ConnectionStringHelper.Create(MySqlClientFactory.Instance, readerConnectionString);

        Assert.True(builder.ContainsKey("Max Pool Size"));
        Assert.Equal("128", builder["Max Pool Size"]?.ToString());
        Assert.True(builder.ContainsKey("Connection Timeout"));
    }

    [Fact]
    public void GetEffectivePoolConfig_MySql_CanonicalMaximumPoolSizeAlias_IsParsed()
    {
        var dialect = new MySqlDialect(MySqlClientFactory.Instance, NullLogger.Instance);
        const string connectionString =
            "Server=localhost;Database=test;User Id=root;Password=pass;Pooling=true;MaximumPoolSize=128;";

        var config = PoolingConfigReader.GetEffectivePoolConfig(dialect, connectionString);

        Assert.Equal(PoolConfigSource.ConnectionString, config.Source);
        Assert.Equal(128, config.MaxPoolSize);
    }

    private sealed class PoolingDialectMissingKeys : SqlDialect
    {
        public PoolingDialectMissingKeys(DbProviderFactory factory) : base(factory, NullLogger.Instance)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
        public override string? PoolingSettingName => " ";
        public override string? MaxPoolSizeSettingName => null;
    }
}
