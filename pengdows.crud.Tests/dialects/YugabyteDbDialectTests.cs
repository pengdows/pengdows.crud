using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Verifies that YugabyteDB-specific dialect behaviour is correct.
/// YugabyteDB is PostgreSQL-compatible and must produce a RETURNING clause
/// on INSERT, not an empty string.
/// </summary>
public class YugabyteDbDialectTests
{
    private const string Cs = "Host=localhost;Database=mydb;Username=u;Password=p;";

    private static YugabyteDbDialect CreateDialect() =>
        new(new fakeDbFactory(SupportedDatabase.YugabyteDb), NullLogger.Instance);

    // Regression: YugabyteDB inherits PostgreSqlDialect but the base SqlDialect
    // switch only matched SupportedDatabase.PostgreSql, leaving YugabyteDb to the
    // wildcard branch which returns string.Empty → INSERT had no RETURNING clause →
    // generated ID was null → fallback to SELECT lastval() → fails on YugabyteDB.
    [Fact]
    public void RenderInsertReturningClause_ReturnsReturningClause()
    {
        var dialect = CreateDialect();

        var result = dialect.RenderInsertReturningClause("\"Id\"");

        Assert.Contains("RETURNING", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Id\"", result);
    }

    [Fact]
    public void SupportsMerge_IsFalse()
    {
        // YugabyteDB 2.x throws OA000 on MERGE despite PostgreSQL 15 base.
        // INSERT ON CONFLICT must be used instead.
        var dialect = CreateDialect();

        Assert.False(dialect.SupportsMerge);
    }

    [Fact]
    public void PrepareStatements_IsFalse()
    {
        // Prepared statements on YugabyteDB can cause "Connection is not open" after
        // transactions complete because prepared statement handles don't survive pool reset.
        var dialect = CreateDialect();

        Assert.False(dialect.PrepareStatements);
    }

    [Fact]
    public void GetBaseSessionSettings_IncludesClientEncoding()
    {
        var dialect = CreateDialect();

        var result = dialect.GetBaseSessionSettings();

        Assert.Contains("client_encoding", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetBaseSessionSettings_IncludesLockTimeout()
    {
        var dialect = CreateDialect();

        var result = dialect.GetBaseSessionSettings();

        Assert.Contains("lock_timeout", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareConnectionStringForDataSource_SetsMaxAutoPrepareToZero()
    {
        // MaxAutoPrepare=0 disables Npgsql auto-prepare, which causes pool-checkout
        // failures on YugabyteDB because prepared statement handles don't survive resets.
        var dialect = CreateDialect();

        var result = dialect.PrepareConnectionStringForDataSource(Cs, readOnly: false);

        Assert.Contains("MaxAutoPrepare=0", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareConnectionStringForDataSource_DoesNotBakeSessionSettings()
    {
        // Unlike PostgreSQL, YugabyteDB's PrepareConnectionStringForDataSource does NOT
        // set the _settingsBaked flag — session settings must be applied on every checkout.
        var dialect = CreateDialect();

        dialect.PrepareConnectionStringForDataSource(Cs, readOnly: false);

        Assert.False(dialect.SessionSettingsBakedIntoDataSource);
    }
}
