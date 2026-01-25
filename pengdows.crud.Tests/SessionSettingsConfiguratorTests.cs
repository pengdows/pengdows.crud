using pengdows.crud.enums;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class SessionSettingsConfiguratorTests
{
    #region GetSessionSettings Tests

    [Fact]
    public void GetSessionSettings_MySql_Standard_ReturnsSettings()
    {
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.MySql,
            DbMode.Standard);

        Assert.Contains("SET SESSION sql_mode", settings);
        Assert.Contains("STRICT_ALL_TABLES", settings);
        Assert.Contains("ANSI_QUOTES", settings);
    }

    [Fact]
    public void GetSessionSettings_MySql_KeepAlive_ReturnsSettings()
    {
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.MySql,
            DbMode.KeepAlive);

        Assert.Contains("SET SESSION sql_mode", settings);
        Assert.Contains("STRICT_ALL_TABLES", settings);
        Assert.Contains("ANSI_QUOTES", settings);
    }

    [Fact]
    public void GetSessionSettings_MariaDb_SingleWriter_ReturnsSettings()
    {
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.MariaDb,
            DbMode.SingleWriter);

        Assert.Contains("SET SESSION sql_mode", settings);
    }

    [Fact]
    public void GetSessionSettings_PostgreSql_KeepAlive_ReturnsSettings()
    {
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.PostgreSql,
            DbMode.KeepAlive);

        Assert.Contains("SET standard_conforming_strings = on", settings);
        Assert.Contains("SET client_min_messages = warning", settings);
    }

    [Fact]
    public void GetSessionSettings_PostgreSql_Standard_ReturnsSettings()
    {
        // PostgreSQL applies settings even in Standard mode
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.PostgreSql,
            DbMode.Standard);

        Assert.Contains("SET standard_conforming_strings", settings);
    }

    [Fact]
    public void GetSessionSettings_CockroachDb_KeepAlive_ReturnsSettings()
    {
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.CockroachDb,
            DbMode.KeepAlive);

        Assert.Contains("SET standard_conforming_strings", settings);
        Assert.Contains("SET client_min_messages", settings);
    }

    [Fact]
    public void GetSessionSettings_Oracle_KeepAlive_ReturnsSettings()
    {
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.Oracle,
            DbMode.KeepAlive);

        Assert.Contains("ALTER SESSION SET NLS_DATE_FORMAT", settings);
    }

    [Fact]
    public void GetSessionSettings_Oracle_Standard_ReturnsSettings()
    {
        // Oracle applies settings even in Standard mode
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.Oracle,
            DbMode.Standard);

        Assert.Contains("ALTER SESSION", settings);
    }

    [Fact]
    public void GetSessionSettings_Sqlite_KeepAlive_ReturnsSettings()
    {
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.Sqlite,
            DbMode.KeepAlive);

        Assert.Contains("PRAGMA foreign_keys = ON", settings);
    }

    [Fact]
    public void GetSessionSettings_Sqlite_Standard_ReturnsSettings()
    {
        // SQLite applies settings even in Standard mode
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.Sqlite,
            DbMode.Standard);

        Assert.Contains("PRAGMA foreign_keys", settings);
    }

    [Fact]
    public void GetSessionSettings_Firebird_KeepAlive_ReturnsEmpty()
    {
        // Firebird settings go in connection string, not session
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.Firebird,
            DbMode.KeepAlive);

        Assert.Empty(settings);
    }

    [Fact]
    public void GetSessionSettings_SqlServer_KeepAlive_ReturnsEmpty()
    {
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.SqlServer,
            DbMode.KeepAlive);

        Assert.Empty(settings);
    }

    [Fact]
    public void GetSessionSettings_DuckDB_KeepAlive_ReturnsEmpty()
    {
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.DuckDB,
            DbMode.KeepAlive);

        Assert.Empty(settings);
    }

    [Fact]
    public void GetSessionSettings_Unknown_ReturnsEmpty()
    {
        var settings = SessionSettingsConfigurator.GetSessionSettings(
            SupportedDatabase.Unknown,
            DbMode.KeepAlive);

        Assert.Empty(settings);
    }

    #endregion

    #region ShouldApplySettings Tests

    [Fact]
    public void ShouldApplySettings_EmptySettings_ReturnsFalse()
    {
        var result = SessionSettingsConfigurator.ShouldApplySettings(string.Empty, DbMode.KeepAlive);
        Assert.False(result);
    }

    [Fact]
    public void ShouldApplySettings_NullSettings_ReturnsFalse()
    {
        var result = SessionSettingsConfigurator.ShouldApplySettings(null, DbMode.KeepAlive);
        Assert.False(result);
    }

    [Fact]
    public void ShouldApplySettings_WhitespaceSettings_ReturnsFalse()
    {
        var result = SessionSettingsConfigurator.ShouldApplySettings("   ", DbMode.KeepAlive);
        Assert.False(result);
    }

    [Fact]
    public void ShouldApplySettings_ValidSettings_StandardMode_ReturnsTrue()
    {
        // Some databases apply settings even in Standard mode
        var result = SessionSettingsConfigurator.ShouldApplySettings("PRAGMA foreign_keys = ON", DbMode.Standard);
        Assert.True(result);
    }

    [Fact]
    public void ShouldApplySettings_ValidSettings_KeepAliveMode_ReturnsTrue()
    {
        var result = SessionSettingsConfigurator.ShouldApplySettings("SET foo = bar", DbMode.KeepAlive);
        Assert.True(result);
    }

    #endregion

    #region ApplySessionSettings Tests

    [Fact]
    public void ApplySessionSettings_EmptySettings_NoOp()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        using var connection = factory.CreateConnection();
        connection.Open();

        // Should not throw
        var result = SessionSettingsConfigurator.ApplySessionSettings(connection, string.Empty);
        Assert.True(result);
    }

    [Fact]
    public void ApplySessionSettings_SingleStatement_Executes()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var connection = factory.CreateConnection();
        connection.Open();

        var result = SessionSettingsConfigurator.ApplySessionSettings(connection, "PRAGMA foreign_keys = ON");
        Assert.True(result);
    }

    [Fact]
    public void ApplySessionSettings_MultipleStatements_Executes()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        using var connection = factory.CreateConnection();
        connection.Open();

        var settings = "SET standard_conforming_strings = on;SET client_min_messages = warning";
        var result = SessionSettingsConfigurator.ApplySessionSettings(connection, settings);
        Assert.True(result);
    }

    [Fact]
    public void ApplySessionSettings_WithWhitespace_HandlesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        using var connection = factory.CreateConnection();
        connection.Open();

        var settings = @"
            SET standard_conforming_strings = on;
            SET client_min_messages = warning;
        ";
        var result = SessionSettingsConfigurator.ApplySessionSettings(connection, settings);
        Assert.True(result);
    }

    [Fact]
    public void ApplySessionSettings_TrailingSemicolon_HandlesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var connection = factory.CreateConnection();
        connection.Open();

        var settings = "PRAGMA foreign_keys = ON;";
        var result = SessionSettingsConfigurator.ApplySessionSettings(connection, settings);
        Assert.True(result);
    }

    #endregion
}