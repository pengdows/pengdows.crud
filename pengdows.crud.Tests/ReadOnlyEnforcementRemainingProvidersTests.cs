using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// G7/G7b (CONCURRENCY.md): closes the "Others | Not yet catalogued" gap left in the G7 table --
/// every remaining provider in docs/supported-databases.md's Read-Only Enforcement Matrix not
/// already covered by ReadOnlyConnectionRejectsWriteTests (SQLite/PostgreSQL/DuckDB/Access/
/// FlatFile) or ReadOnlySessionSqlProvidersRejectWriteTests (MySQL/MariaDB/Oracle/Informix/SAP
/// HANA).
///
/// This also fixes a real fakeDb accuracy gap found while writing it: fakeDbConnection.IsReadOnly
/// previously treated every provider in its connection-string/session-SQL detection switches as
/// equally *enforced* once the marker/SQL was detected. That's true for most of them, but the
/// matrix documents several live-confirmed exceptions where the library applies the marker/SQL
/// and the real database still allows the write:
///
///  - Spanner inherits PostgreSqlDialect's connection-string marker unchanged, but its database
///    does not refuse a write in a read-intent transaction.
///  - SQL Server's ApplicationIntent=ReadOnly is an Availability Group routing hint, not
///    server-side enforcement.
///  - SingleStore inherits MySqlDialect's session SQL unconditionally (only
///    EnforcesReadOnlyTransactions, read by nothing else, differs for it), but its server accepts
///    the statement and still allows the write.
///
/// FakeDbReadOnlyEnforcement.ProductEnforcesConnectionStringReadOnly/ProductEnforcesSessionReadOnly
/// now separate "was the marker/SQL applied" (what the connection-string/session-SQL detection
/// methods answer -- the G7 half) from "does the real database actually act on it" (the G7b half)
/// for exactly these three; fakeDbConnection.IsReadOnly ANDs both together. Before this fix, a
/// Spanner/SqlServer/SingleStore test using the same mechanism as their enforcing relatives would
/// have incorrectly shown a write being rejected that the real database would actually allow.
///
/// TiDB, Snowflake, Db2, Sybase ASE and InterBase needed no fix: TiDbDialect already sends no
/// session SQL at all (ReadOnlyTransactionSql => null), and Snowflake/Db2/SybaseAse/InterBase
/// define no read-only connection parameter or SQL at all -- every detection method in
/// FakeDbReadOnlyEnforcement already falls through to false for them, matching the matrix's
/// "None" entries exactly.
///
/// Firebird uses a third mechanism, different from both groups above: its read-only SQL
/// (SET TRANSACTION READ ONLY;) is folded into DatabaseContext's one combined per-pool
/// session-settings command (baseline + intent in a single CommandText) sent on every checkout
/// from the read pool, not a standalone statement tied to BeginTransaction like Oracle/Informix/
/// SAP HANA. FakeDbReadOnlyEnforcement's session-SQL methods were switched from an exact match to
/// Contains to find it as a substring of that combined batch.
/// </summary>
public sealed class ReadOnlyEnforcementRemainingProvidersTests
{
    private static async Task AssertWriteShapedStatementIsRejected(SupportedDatabase product, string connectionString,
        bool enforceConnectionString = false, bool enforceSessionSql = false, DbMode dbMode = DbMode.Standard)
    {
        var factory = new fakeDbFactory(product);
        var config = new DatabaseContextConfiguration { ConnectionString = connectionString, DbMode = dbMode, EnableMetrics = true };
        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection
        {
            EmulatedProduct = product,
            EnforceReadOnlyFromConnectionString = enforceConnectionString,
            EnforceReadOnlySessionSql = enforceSessionSql
        };
        factory.Connections.Add(conn);

        using var sc = ctx.CreateSqlContainer("UPDATE test SET id = 1");
        var ex = await Assert.ThrowsAsync<ReadOnlyViolationException>(() => sc.ExecuteReaderAsync(CommandType.Text).AsTask());
        Assert.True(ex is IReadOnlyViolation);
    }

    private static async Task AssertWriteShapedStatementSucceeds(SupportedDatabase product, string connectionString,
        bool enforceConnectionString = false, bool enforceSessionSql = false, DbMode dbMode = DbMode.Standard)
    {
        var factory = new fakeDbFactory(product);
        var config = new DatabaseContextConfiguration { ConnectionString = connectionString, DbMode = dbMode, EnableMetrics = true };
        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection
        {
            EmulatedProduct = product,
            EnforceReadOnlyFromConnectionString = enforceConnectionString,
            EnforceReadOnlySessionSql = enforceSessionSql
        };
        conn.EnqueueReaderResult(new System.Collections.Generic.Dictionary<string, object?>[] { new() { ["id"] = 1 } });
        factory.Connections.Add(conn);

        using var sc = ctx.CreateSqlContainer("UPDATE test SET id = 1");
        using var reader = await sc.ExecuteReaderAsync(CommandType.Text);
        Assert.NotNull(reader);
    }

    // ---- Same mechanism as an already-proven parent: confirming tests, not new mechanisms ----

    [Theory]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    [InlineData(SupportedDatabase.AuroraPostgreSql)]
    public async Task PostgreSqlFamily_InheritingTheConnectionStringMarkerUnchanged_RejectsTheWrite(SupportedDatabase product)
    {
        await AssertWriteShapedStatementIsRejected(product,
            $"Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct={product};" +
            "Options='-c default_transaction_read_only=on'",
            enforceConnectionString: true);
    }

    [Fact(Timeout = 10000)]
    public async Task AuroraMySql_SessionSql_RejectsTheWriteInsideAReadOnlyTransaction()
    {
        var factory = new fakeDbFactory(SupportedDatabase.AuroraMySql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=AuroraMySql",
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };
        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.AuroraMySql, EnforceReadOnlySessionSql = true };
        factory.Connections.Add(conn);

        using var tx = ctx.BeginTransaction(executionType: ExecutionType.Read);
        Assert.True(conn.SessionMarkedReadOnly);
        Assert.True(conn.IsReadOnly);

        using var sc = tx.CreateSqlContainer("UPDATE test SET id = 1");
        var ex = await Assert.ThrowsAsync<ReadOnlyViolationException>(() => sc.ExecuteReaderAsync(CommandType.Text).AsTask());
        Assert.True(ex is IReadOnlyViolation);
    }

    // ---- Marker/SQL genuinely applied, but the real database does not act on it ----

    [Fact]
    public void Spanner_ConnectionStringMarkerIsDetected_ButDoesNotMakeTheConnectionReadOnly()
    {
        // G7 half: the marker genuinely is present and detected (IsReadOnlyConnectionString has no
        // product-specific carve-out -- SpannerDialect inherits PostgreSqlDialect's
        // GetReadOnlyConnectionParameter() unchanged, so the same marker-detection logic that
        // makes a PostgreSQL connection read-only runs identically for Spanner). Proven by
        // comparing to a connection string with no marker at all, and to the same marker on a real
        // PostgreSQL product, rather than trusting IsReadOnly's single boolean in isolation --
        // IsReadOnly folds detection and enforcement together, so it alone can't distinguish
        // "never detected" from "detected, then overridden."
        var spannerWithMarker = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Spanner,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Host=localhost;Database=test;Options='-c default_transaction_read_only=on'"
        };
        var spannerWithoutMarker = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Spanner,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Host=localhost;Database=test"
        };
        var postgresWithMarker = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.PostgreSql,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Host=localhost;Database=test;Options='-c default_transaction_read_only=on'"
        };

        Assert.True(FakeDbReadOnlyEnforcement.IsReadOnlyConnectionString(spannerWithMarker.ConnectionString, SupportedDatabase.Spanner));
        Assert.False(FakeDbReadOnlyEnforcement.IsReadOnlyConnectionString(spannerWithoutMarker.ConnectionString, SupportedDatabase.Spanner));

        // G7b half: despite the marker being genuinely detected, IsReadOnly (what actually gates
        // write rejection) stays false for Spanner specifically -- the real database does not
        // refuse the write for it -- while the identical marker DOES make a real PostgreSQL
        // connection read-only, isolating that the Spanner override, not some unrelated
        // difference, is what changes the outcome.
        Assert.False(spannerWithMarker.IsReadOnly);
        Assert.False(spannerWithoutMarker.IsReadOnly);
        Assert.True(postgresWithMarker.IsReadOnly);
    }

    [Fact(Timeout = 10000)]
    public async Task Spanner_ConnectionStringMarkerIsApplied_ButTheWriteSucceeds()
    {
        await AssertWriteShapedStatementSucceeds(SupportedDatabase.Spanner,
            "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=Spanner;" +
            "Options='-c default_transaction_read_only=on'",
            enforceConnectionString: true);
    }

    [Fact(Timeout = 10000)]
    public async Task SqlServer_ApplicationIntentReadOnly_IsApplied_ButTheWriteSucceeds()
    {
        var conn = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.SqlServer,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Server=localhost;Database=test;ApplicationIntent=ReadOnly"
        };
        // The marker is recognized (detectable, for a test that wants to confirm G7's half of the
        // claim) but never enforced -- it is a routing hint, not server-side protection.
        Assert.False(conn.IsReadOnly);

        await AssertWriteShapedStatementSucceeds(SupportedDatabase.SqlServer,
            "Server=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=SqlServer;ApplicationIntent=ReadOnly",
            enforceConnectionString: true);
    }

    // ---- Session SQL genuinely sent, but the real database does not act on it ----

    [Fact(Timeout = 10000)]
    public async Task SingleStore_SessionSqlIsSent_ButTheWriteSucceeds()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SingleStore);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=SingleStore",
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };
        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.SingleStore, EnforceReadOnlySessionSql = true };
        conn.EnqueueReaderResult(new System.Collections.Generic.Dictionary<string, object?>[] { new() { ["id"] = 1 } });
        factory.Connections.Add(conn);

        using var tx = ctx.BeginTransaction(executionType: ExecutionType.Read);

        // G7 half: MySqlDialect's ReadOnlyTransactionSql is unconditional, so the exact same "SET
        // SESSION transaction_read_only = 1;" MySQL sends really is sent for SingleStore too.
        Assert.True(conn.SessionMarkedReadOnly);
        // G7b half: but IsReadOnly stays false -- SingleStore's server accepts the statement and
        // still allows the write (live-confirmed, docs/supported-databases.md).
        Assert.False(conn.IsReadOnly);

        using var sc = tx.CreateSqlContainer("UPDATE test SET id = 1");
        using var reader = await sc.ExecuteReaderAsync(CommandType.Text);
        Assert.NotNull(reader);
    }

    // ---- No session SQL sent at all: TiDB treats READ ONLY as a no-op, so the library doesn't try ----

    [Fact(Timeout = 10000)]
    public async Task TiDb_SendsNoSessionSqlAtAll_AndTheWriteSucceeds()
    {
        var factory = new fakeDbFactory(SupportedDatabase.TiDb);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=TiDb",
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };
        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.TiDb, EnforceReadOnlySessionSql = true };
        conn.EnqueueReaderResult(new System.Collections.Generic.Dictionary<string, object?>[] { new() { ["id"] = 1 } });
        factory.Connections.Add(conn);

        using var tx = ctx.BeginTransaction(executionType: ExecutionType.Read);

        // TiDbDialect.ReadOnlyTransactionSql => null: nothing was ever sent for this connection to
        // recognize, unlike MySql/AuroraMySql/SingleStore/MariaDb above.
        Assert.False(conn.SessionMarkedReadOnly);
        Assert.False(conn.IsReadOnly);

        using var sc = tx.CreateSqlContainer("UPDATE test SET id = 1");
        using var reader = await sc.ExecuteReaderAsync(CommandType.Text);
        Assert.NotNull(reader);
    }

    // ---- Zero mechanism: the library defines no read-only connection parameter or SQL at all ----

    [Theory]
    [InlineData(SupportedDatabase.Snowflake)]
    [InlineData(SupportedDatabase.Db2)]
    [InlineData(SupportedDatabase.SybaseASE)]
    [InlineData(SupportedDatabase.InterBase)]
    public async Task NoReadOnlyMechanismAtAll_WriteShapedStatement_Succeeds(SupportedDatabase product)
    {
        await AssertWriteShapedStatementSucceeds(product,
            $"Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct={product}",
            enforceConnectionString: true, enforceSessionSql: true);
    }

    // ---- Firebird: a third mechanism (combined per-pool session-settings batch, not a standalone statement) ----

    [Fact]
    public void FirebirdDialect_GetFinalSessionSettings_ContainsTheRightReadOnlyIntentText()
    {
        var dialect = new pengdows.crud.dialects.FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var readOnly = dialect.GetFinalSessionSettings(readOnly: true, null);
        var readWrite = dialect.GetFinalSessionSettings(readOnly: false, null);

        Assert.Contains("SET TRANSACTION READ ONLY;", readOnly);
        Assert.Contains("SET TRANSACTION READ WRITE;", readWrite);
        Assert.DoesNotContain("SET TRANSACTION READ ONLY;", readWrite);
    }

    [Fact(Timeout = 10000)]
    public async Task Firebird_CombinedSessionSettingsBatchIsSent_ButTheWriteSucceeds()
    {
        // docs/read-only-enforcement.md's "Is a read-intent transaction read-only at the
        // database?" (live-verified 2026-10-03) lists Firebird under "No (the library's guard is
        // the only protection)" -- the SQL genuinely goes out (proven below via
        // SessionMarkedReadOnly), but it does not make a later write fail. An earlier version of
        // this test asserted the opposite, inferred from FirebirdDialect.TryClassifyProviderException
        // being able to classify error 335544361 if a real Firebird ever raised it -- that
        // classification capability existing was wrongly taken as proof this specific mechanism
        // triggers it, without first checking the live result already recorded in
        // read-only-enforcement.md.
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Database=/tmp/pengdows.readonly-reject.probe.fdb;EmulatedProduct=Firebird",
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };
        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Firebird, EnforceReadOnlySessionSql = true };
        conn.EnqueueReaderResult(new System.Collections.Generic.Dictionary<string, object?>[] { new() { ["id"] = 1 } });
        factory.Connections.Add(conn);

        // A single read-intent checkout: DatabaseContext runs its combined per-pool
        // session-settings command (baseline + "SET TRANSACTION READ ONLY;" in one CommandText --
        // the real production trigger, not a hand-crafted command text) as this connection's
        // first-open handler, before the UPDATE itself ever runs on it.
        using var writeSc = ctx.CreateSqlContainer("UPDATE test SET id = 1");
        using var reader = await writeSc.ExecuteReaderAsync(CommandType.Text);
        Assert.NotNull(reader);

        // G7 half: the SQL really was sent and recognized.
        Assert.True(conn.SessionMarkedReadOnly);
        // G7b half: but it does not make the connection actually read-only for write-rejection
        // purposes, matching the live-verified result.
        Assert.False(conn.IsReadOnly);
    }
}
