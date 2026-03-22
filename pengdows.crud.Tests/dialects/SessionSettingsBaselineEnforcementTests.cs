using System;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Verifies that dialects always enforce a full session-settings baseline on every
/// connection checkout, even when the initial detection found the first pooled
/// connection already compliant (cached diff == "").
///
/// Without this guarantee, a subsequent pooled connection whose session state was
/// mutated by external code would silently drift from the expected baseline.
/// </summary>
public class SessionSettingsBaselineEnforcementTests
{
    // Reflection is required here because there is no public API to drive the dialect cache
    // into a stale/empty state. The test simulates a scenario that occurs only at runtime
    // (detection found the first pooled connection already compliant → cached diff = "").
    // Directly mutating the private field is the minimal way to reproduce this boundary
    // condition without adding test-only hooks to production code.
    private static readonly BindingFlags NonPublicInstance =
        BindingFlags.NonPublic | BindingFlags.Instance;

    // ──────────────────────────────────────────────
    //  PostgreSQL
    // ──────────────────────────────────────────────

    [Fact]
    public void PostgreSql_GetBaseSessionSettings_WhenCacheIsEmpty_StillReturnsFullBaseline()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger<PostgreSqlDialect>.Instance);

        // Simulate: detection ran, first connection was already compliant → cached ""
        var field = typeof(PostgreSqlDialect).GetField("_sessionSettings", NonPublicInstance);
        field!.SetValue(dialect, string.Empty);

        var settings = dialect.GetBaseSessionSettings();

        // Must still contain the full baseline, not ""
        Assert.False(string.IsNullOrWhiteSpace(settings),
            "GetBaseSessionSettings must return a non-empty baseline even when cached diff is empty");
        Assert.Contains("standard_conforming_strings", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client_min_messages", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSql_GetConnectionSessionSettings_WhenCacheIsEmpty_EnforcesBaseline()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger<PostgreSqlDialect>.Instance);

        var field = typeof(PostgreSqlDialect).GetField("_sessionSettings", NonPublicInstance);
        field!.SetValue(dialect, string.Empty);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(config, factory);

        var settings = dialect.GetConnectionSessionSettings(ctx, false);

        Assert.False(string.IsNullOrWhiteSpace(settings),
            "GetConnectionSessionSettings must return enforcement SQL even when diff cache is empty");
        Assert.Contains("standard_conforming_strings", settings, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────
    //  SQL Server
    // ──────────────────────────────────────────────

    [Fact]
    public void SqlServer_GetBaseSessionSettings_WhenCacheIsEmpty_StillReturnsFullBaseline()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);

        var field = typeof(SqlServerDialect).GetField("_sessionSettings", NonPublicInstance);
        field!.SetValue(dialect, string.Empty);

        var settings = dialect.GetBaseSessionSettings();

        Assert.False(string.IsNullOrWhiteSpace(settings),
            "GetBaseSessionSettings must return a non-empty baseline even when cached diff is empty");
        Assert.Contains("SET QUOTED_IDENTIFIER ON", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET ANSI_NULLS ON", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET ARITHABORT ON", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServer_GetConnectionSessionSettings_WhenCacheIsEmpty_EnforcesBaseline()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);

        var field = typeof(SqlServerDialect).GetField("_sessionSettings", NonPublicInstance);
        field!.SetValue(dialect, string.Empty);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(config, factory);

        var settings = dialect.GetConnectionSessionSettings(ctx, false);

        Assert.False(string.IsNullOrWhiteSpace(settings),
            "GetConnectionSessionSettings must return enforcement SQL even when diff cache is empty");
        Assert.Contains("QUOTED_IDENTIFIER", settings, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────
    //  Null cache (pre-detection) should still work
    // ──────────────────────────────────────────────

    [Fact]
    public void PostgreSql_GetBaseSessionSettings_WhenCacheIsNull_ReturnsFallbackBaseline()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger<PostgreSqlDialect>.Instance);

        // _sessionSettings is null by default (no detection yet)
        var settings = dialect.GetBaseSessionSettings();

        Assert.False(string.IsNullOrWhiteSpace(settings));
        Assert.Contains("standard_conforming_strings", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServer_GetBaseSessionSettings_WhenCacheIsNull_ReturnsFallbackBaseline()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);

        // _sessionSettings is null by default (no detection yet)
        var settings = dialect.GetBaseSessionSettings();

        Assert.False(string.IsNullOrWhiteSpace(settings));
        Assert.Contains("QUOTED_IDENTIFIER", settings, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────
    //  Firebird — base-class GetFinalSessionSettings path
    // ──────────────────────────────────────────────

    [Fact]
    public void Firebird_GetFinalSessionSettings_ReadOnly_IncludesBaselineAndReadOnlyIntent()
    {
        // Firebird does not override GetFinalSessionSettings; the base-class combines
        // GetBaseSessionSettings() and GetReadOnlySessionSettings() into one batch.
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird), NullLogger<FirebirdDialect>.Instance);

        var settings = dialect.GetFinalSessionSettings(readOnly: true);

        Assert.Contains("SET NAMES UTF8", settings, StringComparison.Ordinal);
        Assert.Contains("SET SQL DIALECT 3", settings, StringComparison.Ordinal);
        Assert.Contains("SET TRANSACTION READ ONLY", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Firebird_GetFinalSessionSettings_ReadWrite_IncludesBaselineAndReadWriteReset()
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird), NullLogger<FirebirdDialect>.Instance);

        var settings = dialect.GetFinalSessionSettings(readOnly: false);

        Assert.Contains("SET NAMES UTF8", settings, StringComparison.Ordinal);
        Assert.Contains("SET SQL DIALECT 3", settings, StringComparison.Ordinal);
        Assert.Contains("SET TRANSACTION READ WRITE", settings, StringComparison.Ordinal);
    }
}