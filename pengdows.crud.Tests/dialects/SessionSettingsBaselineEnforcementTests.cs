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
///
/// SQL SERVER IS A DELIBERATE EXCEPTION to this invariant (see SqlServerDialect.cs's
/// SessionSettingsDef investigation-trail comment): live testing against real SQL Server 2017
/// and 2022 engines confirmed the driver's login sequence plus sp_reset_connection already
/// guarantee all seven settings unconditionally at database compatibility level >= 90 — a level
/// no SQL Server version this dialect targets can even fall below. GetSqlServerSessionSettings
/// now produces a genuinely empty cached diff only after a live compatibility-level check
/// confirms this, and GetBaseSessionSettings honors that empty result instead of coercing it
/// back to the baseline. The other dialects here (PostgreSQL, Firebird) have not been
/// re-investigated and keep the original "always enforce" contract.
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

    // SQL Server deliberately diverges here: an empty cached diff is only ever produced by
    // GetSqlServerSessionSettings after a live compatibility-level check confirms >= 90 (see
    // SqlServerDialectSettingsTests.GetConnectionSessionSettings_ModernCompatibilityLevel_
    // ReturnsNoSessionSettings for the production-path test). GetBaseSessionSettings must honor
    // that, not coerce it back to the baseline — the NULL case (detection never ran) is the one
    // that still falls back, covered below by SqlServer_GetBaseSessionSettings_WhenCacheIsNull_
    // ReturnsFallbackBaseline.
    [Fact]
    public void SqlServer_GetBaseSessionSettings_WhenCacheIsEmpty_HonorsIntentionalEmptyResult()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);

        var field = typeof(SqlServerDialect).GetField("_sessionSettings", NonPublicInstance);
        field!.SetValue(dialect, string.Empty);

        var settings = dialect.GetBaseSessionSettings();

        Assert.True(string.IsNullOrWhiteSpace(settings),
            "An intentionally empty cached diff (modern compatibility level confirmed) must not " +
            "be coerced back into the full baseline");
    }

    [Fact]
    public void SqlServer_GetConnectionSessionSettings_WhenCacheIsEmpty_ProducesNoSettings()
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

        Assert.True(string.IsNullOrWhiteSpace(settings),
            "An intentionally empty cached diff must propagate through to GetConnectionSessionSettings");
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