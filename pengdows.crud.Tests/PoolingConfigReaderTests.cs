using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.fakeDb;
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
    public void GetEffectivePoolConfig_WhenPoolingDisabled_TreatsProviderPoolAsUnbounded()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);

        var cs =
            "Host=localhost;Database=postgres;Username=postgres;Password=x;Pooling=false;Minimum Pool Size=2;Maximum Pool Size=42;";
        var cfg = PoolingConfigReader.GetEffectivePoolConfig(dialect, cs);

        Assert.Equal(PoolConfigSource.ConnectionString, cfg.Source);
        Assert.False(cfg.PoolingEnabled);
        Assert.Equal(2, cfg.MinPoolSize);
        Assert.Null(cfg.MaxPoolSize);
    }

    [Fact]
    public void GetEffectivePoolConfig_WhenDialectDoesNotSupportExternalPooling_ReturnsDialectDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var dialect = new DuckDbDialect(factory, NullLogger.Instance);

        var cfg = PoolingConfigReader.GetEffectivePoolConfig(dialect, "DataSource=:memory:");

        Assert.Equal(PoolConfigSource.DialectDefault, cfg.Source);
        Assert.Null(cfg.PoolingEnabled);
        Assert.NotNull(cfg.MinPoolSize);
        Assert.NotNull(cfg.MaxPoolSize);
    }
}