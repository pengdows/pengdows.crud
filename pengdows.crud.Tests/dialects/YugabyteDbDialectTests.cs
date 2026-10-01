using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
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

    [Fact]
    public async Task SupportsXmlTypes_IsTrue()
    {
        // Unlike CockroachDB, YugabyteDB's YSQL layer genuinely reuses PostgreSQL's own query
        // layer - confirmed live against a real yugabytedb/yugabyte container (v2.x):
        // `CREATE TABLE t (x xml)` succeeded. Correctly inherited from PostgreSqlDialect, which
        // gates this on IsVersionAtLeast(8, 3) - so the dialect must be initialized with a real
        // version for this to resolve true (pre-init, like every other IsVersionAtLeast-gated
        // flag, it correctly reports false).
        var factory = new fakeDbFactory(SupportedDatabase.YugabyteDb);
        var conn = factory.CreateConnection();
        conn.ConnectionString = "Host=localhost;EmulatedProduct=YugabyteDb";
        var scalars = new Dictionary<string, object>
        {
            ["SELECT version()"] = "PostgreSQL 15.12-YB-2.25.2.0-b0 on x86_64-pc-linux-gnu"
        };
        var schema = DataSourceInformation.BuildEmptySchema(
            "PostgreSQL", "15.12", "@p[0-9]+", "@{0}", 63, @"@\w+", @"@\w+", true);
        var tracked = new FakeTrackedConnection(conn, schema, scalars);
        var dialect = CreateDialect();
        await dialect.DetectDatabaseInfoAsync(tracked);

        Assert.True(dialect.SupportsXmlTypes);
    }

    [Fact]
    public void SupportsUserDefinedTypes_IsTrue()
    {
        // Confirmed live: `CREATE TYPE my_udt AS (a int, b text)` succeeded against a real
        // YugabyteDB container. Correctly inherited from PostgreSqlDialect.
        Assert.True(CreateDialect().SupportsUserDefinedTypes);
    }

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
