using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class PoolingDefaultsTests
{
    /// <summary>
    /// Tests that SQL Server gets correct pooling defaults for pooling settings
    /// </summary>
    [Fact]
    public void SqlServerDialect_HasCorrectPoolingDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var logger = NullLogger<SqlDialect>.Instance;
        var dialect = new SqlServerDialect(factory, logger);

        Assert.True(dialect.SupportsExternalPooling);
        Assert.Equal("Pooling", dialect.PoolingSettingName);
        Assert.Equal("Min Pool Size", dialect.MinPoolSizeSettingName);
        Assert.Equal("Max Pool Size", dialect.MaxPoolSizeSettingName);
    }

    /// <summary>
    /// Tests that PostgreSQL gets correct pooling defaults for pooling settings
    /// </summary>
    [Fact]
    public void PostgreSqlDialect_HasCorrectPoolingDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var logger = NullLogger<SqlDialect>.Instance;
        var dialect = new PostgreSqlDialect(factory, logger);

        Assert.True(dialect.SupportsExternalPooling);
        Assert.Equal("Pooling", dialect.PoolingSettingName);
        Assert.Equal("Minimum Pool Size", dialect.MinPoolSizeSettingName);
        Assert.Equal("Maximum Pool Size", dialect.MaxPoolSizeSettingName);
    }

    /// <summary>
    /// Tests that MySQL gets correct pooling defaults (default provider behavior)
    /// </summary>
    [Fact]
    public void MySqlDialect_HasCorrectPoolingDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var logger = NullLogger<SqlDialect>.Instance;
        var dialect = new MySqlDialect(factory, logger);

        Assert.True(dialect.SupportsExternalPooling);
        Assert.Equal("Pooling", dialect.PoolingSettingName);
        // Since it's not MySqlConnector, should use Oracle provider format
        Assert.Equal("Min Pool Size", dialect.MinPoolSizeSettingName);
        Assert.Equal("Max Pool Size", dialect.MaxPoolSizeSettingName);
    }

    /// <summary>
    /// Tests that MariaDB gets correct pooling defaults
    /// </summary>
    [Fact]
    public void MariaDbDialect_HasCorrectPoolingDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var logger = NullLogger<SqlDialect>.Instance;
        var dialect = new MariaDbDialect(factory, logger);

        Assert.True(dialect.SupportsExternalPooling);
        Assert.Equal("Pooling", dialect.PoolingSettingName);
        Assert.Equal("Min Pool Size", dialect.MinPoolSizeSettingName);
        Assert.Equal("Max Pool Size", dialect.MaxPoolSizeSettingName);
    }

    /// <summary>
    /// Tests that Oracle gets correct pooling defaults
    /// </summary>
    [Fact]
    public void OracleDialect_HasCorrectPoolingDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        var logger = NullLogger<SqlDialect>.Instance;
        var dialect = new OracleDialect(factory, logger);

        Assert.True(dialect.SupportsExternalPooling);
        Assert.Equal("Pooling", dialect.PoolingSettingName);
        Assert.Equal("Min Pool Size", dialect.MinPoolSizeSettingName);
        Assert.Equal("Max Pool Size", dialect.MaxPoolSizeSettingName);
    }

    /// <summary>
    /// Tests that Firebird gets correct pooling defaults
    /// </summary>
    [Fact]
    public void FirebirdDialect_HasCorrectPoolingDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var logger = NullLogger<SqlDialect>.Instance;
        var dialect = new FirebirdDialect(factory, logger);

        Assert.True(dialect.SupportsExternalPooling);
        Assert.Equal("Pooling", dialect.PoolingSettingName);
        Assert.Equal("MinPoolSize", dialect.MinPoolSizeSettingName);
        Assert.Equal("MaxPoolSize", dialect.MaxPoolSizeSettingName);
    }

    /// <summary>
    /// Tests that SQLite gets correct pooling defaults (default provider behavior)
    /// </summary>
    [Fact]
    public void SqliteDialect_HasCorrectPoolingDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var logger = NullLogger<SqlDialect>.Instance;
        var dialect = new SqliteDialect(factory, logger);

        // Since it's not System.Data.SQLite, should be false for external pooling
        Assert.False(dialect.SupportsExternalPooling);
        Assert.Equal("Pooling", dialect.PoolingSettingName);
        Assert.Null(dialect.MinPoolSizeSettingName); // no min keyword for either
        Assert.Null(dialect.MaxPoolSizeSettingName);
    }

    /// <summary>
    /// Tests that DuckDB has no pooling support (in-process database)
    /// </summary>
    [Fact]
    public void DuckDbDialect_HasNoPoolingSupport()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var logger = NullLogger<SqlDialect>.Instance;
        var dialect = new DuckDbDialect(factory, logger);

        Assert.False(dialect.SupportsExternalPooling); // in-process
        Assert.Null(dialect.PoolingSettingName);
        Assert.Null(dialect.MinPoolSizeSettingName);
        Assert.Null(dialect.MaxPoolSizeSettingName);
    }
}
