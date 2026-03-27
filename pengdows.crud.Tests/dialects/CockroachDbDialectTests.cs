using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Verifies that CockroachDB-specific startup settings (client_encoding, lock_timeout)
/// are baked into the Options parameter alongside the inherited PostgreSQL base settings,
/// so that the per-checkout SET round-trip skip is safe for all CockroachDB settings.
/// </summary>
public class CockroachDbDialectTests
{
    private const string Cs = "Host=localhost;Database=mydb;Username=u;Password=p;";

    private static CockroachDbDialect CreateDialect() =>
        new(new fakeDbFactory(SupportedDatabase.CockroachDb), NullLogger.Instance);

    [Fact]
    public void PrepareConnectionStringForDataSource_BakesBasePostgreSqlSettings()
    {
        var dialect = CreateDialect();

        var result = dialect.PrepareConnectionStringForDataSource(Cs, readOnly: false);

        Assert.Contains("standard_conforming_strings=on", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client_min_messages=warning", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("default_transaction_read_only=off", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareConnectionStringForDataSource_BakesClientEncoding()
    {
        var dialect = CreateDialect();

        var result = dialect.PrepareConnectionStringForDataSource(Cs, readOnly: false);

        Assert.Contains("client_encoding=UTF8", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareConnectionStringForDataSource_BakesLockTimeout()
    {
        var dialect = CreateDialect();

        var result = dialect.PrepareConnectionStringForDataSource(Cs, readOnly: false);

        Assert.Contains("lock_timeout=30s", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareConnectionStringForDataSource_ReadOnly_BakesReadOnlyFlag()
    {
        var dialect = CreateDialect();

        var result = dialect.PrepareConnectionStringForDataSource(Cs, readOnly: true);

        Assert.Contains("default_transaction_read_only=on", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("default_transaction_read_only=off", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareConnectionStringForDataSource_SetsSessionSettingsBakedFlag()
    {
        var dialect = CreateDialect();

        dialect.PrepareConnectionStringForDataSource(Cs, readOnly: false);

        Assert.True(dialect.SessionSettingsBakedIntoDataSource);
    }

    [Fact]
    public void PrepareConnectionStringForDataSource_PreservesUserOptions()
    {
        var dialect = CreateDialect();
        var cs = Cs + "Options=-c search_path=crdb_schema;";

        var result = dialect.PrepareConnectionStringForDataSource(cs, readOnly: false);

        Assert.Contains("search_path=crdb_schema", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client_encoding=UTF8", result, StringComparison.OrdinalIgnoreCase);
    }

    // Regression: CockroachDB inherits PostgreSqlDialect but the base SqlDialect
    // switch only matched SupportedDatabase.PostgreSql, leaving CockroachDb to the
    // wildcard branch which returns string.Empty → INSERT had no RETURNING clause →
    // generated ID was null → fallback to SELECT lastval() → fails on CockroachDB.
    [Fact]
    public void RenderInsertReturningClause_ReturnsReturningClause()
    {
        var dialect = CreateDialect();

        var result = dialect.RenderInsertReturningClause("\"Id\"");

        Assert.Contains("RETURNING", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Id\"", result);
    }
}
