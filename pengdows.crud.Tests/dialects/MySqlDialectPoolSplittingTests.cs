using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Tests for MySQL/MariaDB provider-aware pool splitting behavior.
/// Oracle's MySql.Data does not support Application Name in connection strings,
/// so pool separation uses a Connection Timeout delta instead.
/// MySqlConnector supports Application Name and uses the standard :ro/:rw suffix.
/// </summary>
public class MySqlDialectPoolSplittingTests
{
    #region MySqlConnector path

    [Fact]
    public void MySqlConnector_ApplicationNameSettingName_ReturnsApplicationName()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);

        Assert.Equal("Application Name", dialect.ApplicationNameSettingName);
    }

    [Fact]
    public void MySqlConnector_GetReadOnlyConnectionString_ReturnsUnchanged()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);

        var cs = "Server=db;Database=app;User Id=sa;Password=secret";
        var result = dialect.GetReadOnlyConnectionString(cs);

        Assert.Equal(cs, result);
    }

    #endregion

    #region Oracle MySql.Data path

    [Fact]
    public void OracleMySqlData_ApplicationNameSettingName_ReturnsNull()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: false);

        Assert.Null(dialect.ApplicationNameSettingName);
    }

    [Fact]
    public void OracleMySqlData_GetReadOnlyConnectionString_AddsTimeoutDelta()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: false);

        var cs = "Server=db;Database=app;User Id=sa;Password=secret";
        var result = dialect.GetReadOnlyConnectionString(cs);

        // Should contain Connection Timeout = default (15) + 1 = 16
        Assert.Contains("Connection Timeout", result, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(cs, result);
    }

    [Fact]
    public void OracleMySqlData_GetReadOnlyConnectionString_WithExistingTimeout_IncrementsBy1()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: false);

        var cs = "Server=db;Database=app;Connection Timeout=30";
        var result = dialect.GetReadOnlyConnectionString(cs);

        // Should increment existing timeout by 1
        Assert.Contains("31", result);
    }

    [Fact]
    public void OracleMySqlData_GetReadOnlyConnectionString_WithNoTimeout_UsesDefaultPlus1()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: false);

        var cs = "Server=db;Database=app";
        var result = dialect.GetReadOnlyConnectionString(cs);

        // Default MySQL timeout is 15, so result should contain 16
        Assert.Contains("16", result);
    }

    [Fact]
    public void OracleMySqlData_GetReadOnlyConnectionString_EmptyString_ReturnsEmpty()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: false);

        var result = dialect.GetReadOnlyConnectionString(string.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void OracleMySqlData_GetReadOnlyConnectionString_NullString_ReturnsNull()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: false);

        var result = dialect.GetReadOnlyConnectionString(null!);

        Assert.Null(result);
    }

    #endregion

    #region Default constructor (fakeDb namespace → not MySqlConnector)

    [Fact]
    public void DefaultConstructor_WithFakeDb_DetectsNotMySqlConnector()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);

        // fakeDb namespace doesn't contain "MySqlConnector", so should be treated as Oracle MySql.Data
        Assert.Null(dialect.ApplicationNameSettingName);
    }

    #endregion
}

/// <summary>
/// Tests for MariaDB dialect inheriting provider-aware pool splitting from MySqlDialect.
/// </summary>
public class MariaDbDialectPoolSplittingTests
{
    #region MySqlConnector path

    [Fact]
    public void MySqlConnector_ApplicationNameSettingName_ReturnsApplicationName()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var dialect = new MariaDbDialect(factory, NullLogger<MariaDbDialect>.Instance, isMySqlConnector: true);

        Assert.Equal("Application Name", dialect.ApplicationNameSettingName);
    }

    [Fact]
    public void MySqlConnector_GetReadOnlyConnectionString_ReturnsUnchanged()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var dialect = new MariaDbDialect(factory, NullLogger<MariaDbDialect>.Instance, isMySqlConnector: true);

        var cs = "Server=db;Database=app;User Id=sa;Password=secret";
        var result = dialect.GetReadOnlyConnectionString(cs);

        Assert.Equal(cs, result);
    }

    #endregion

    #region Oracle MySql.Data path

    [Fact]
    public void OracleMySqlData_ApplicationNameSettingName_ReturnsNull()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var dialect = new MariaDbDialect(factory, NullLogger<MariaDbDialect>.Instance, isMySqlConnector: false);

        Assert.Null(dialect.ApplicationNameSettingName);
    }

    [Fact]
    public void OracleMySqlData_GetReadOnlyConnectionString_AddsTimeoutDelta()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var dialect = new MariaDbDialect(factory, NullLogger<MariaDbDialect>.Instance, isMySqlConnector: false);

        var cs = "Server=db;Database=app;User Id=sa;Password=secret";
        var result = dialect.GetReadOnlyConnectionString(cs);

        Assert.Contains("Connection Timeout", result, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(cs, result);
    }

    [Fact]
    public void OracleMySqlData_GetReadOnlyConnectionString_WithExistingTimeout_IncrementsBy1()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var dialect = new MariaDbDialect(factory, NullLogger<MariaDbDialect>.Instance, isMySqlConnector: false);

        var cs = "Server=db;Database=app;Connection Timeout=30";
        var result = dialect.GetReadOnlyConnectionString(cs);

        Assert.Contains("31", result);
    }

    #endregion

    #region Default constructor inherits correctly

    [Fact]
    public void DefaultConstructor_WithFakeDb_DetectsNotMySqlConnector()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var dialect = new MariaDbDialect(factory, NullLogger<MariaDbDialect>.Instance);

        // fakeDb namespace doesn't contain "MySqlConnector", so inherits Oracle MySql.Data path
        Assert.Null(dialect.ApplicationNameSettingName);
    }

    #endregion
}
