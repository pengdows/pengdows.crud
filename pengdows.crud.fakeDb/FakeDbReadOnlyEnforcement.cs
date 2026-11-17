using System;
using System.Data.Common;
using pengdows.crud.enums;

namespace pengdows.crud.fakeDb;

/// <summary>
/// Detects the real per-product read-only connection-string marker a pengdows.crud dialect would
/// have applied for read intent, so fakeDbConnection.IsReadOnly reflects reality instead of
/// needing a test to set a flag by hand. Markers and their sources: docs/read-only-enforcement.md
/// and docs/supported-databases.md's Read-Only Enforcement Matrix, cross-checked against each
/// dialect's GetReadOnlyConnectionParameter().
/// </summary>
internal static class FakeDbReadOnlyEnforcement
{
    internal static bool IsReadOnlyConnectionString(string? connectionString, SupportedDatabase product)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        DbConnectionStringBuilder builder;
        try
        {
            builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch (ArgumentException)
        {
            // Not a key/value connection string (e.g. a raw provider-specific string the builder
            // can't parse) -- fall back to false rather than guessing with a substring search.
            return false;
        }

        switch (product)
        {
            case SupportedDatabase.Sqlite:
                // SqliteDialect.GetReadOnlyConnectionParameter() -> "Mode=ReadOnly". Not applied to
                // :memory:/shared-cache databases by the dialect itself (docs/supported-databases.md),
                // but that is the dialect's own decision, not something fakeDb needs to re-derive here
                // -- if the key isn't in the string, this correctly reports false.
                return builder.TryGetValue("Mode", out var sqliteMode) &&
                       string.Equals(sqliteMode as string, "ReadOnly", StringComparison.OrdinalIgnoreCase);

            case SupportedDatabase.Access:
                // AccessDialect.GetReadOnlyConnectionParameter() -> "Mode=Read". Exact match only --
                // "Mode=ReadWrite" is a different value for the same key, not a read-only connection.
                return builder.TryGetValue("Mode", out var accessMode) &&
                       string.Equals(accessMode as string, "Read", StringComparison.OrdinalIgnoreCase);

            case SupportedDatabase.DuckDB:
                // DuckDbDialect's read-only file-open attribute.
                return builder.TryGetValue("access_mode", out var duckMode) &&
                       string.Equals(duckMode as string, "READ_ONLY", StringComparison.OrdinalIgnoreCase);

            case SupportedDatabase.FlatFile:
                return builder.TryGetValue("readonly", out var flatFileReadOnly) &&
                       string.Equals(flatFileReadOnly as string, "true", StringComparison.OrdinalIgnoreCase);

            case SupportedDatabase.PostgreSql:
            case SupportedDatabase.CockroachDb:
            case SupportedDatabase.YugabyteDb:
            case SupportedDatabase.Spanner:
            case SupportedDatabase.AuroraPostgreSql:
                // PostgreSqlDialect.GetReadOnlyConnectionParameter() embeds the GUC inside the
                // Options= value (Options='-c default_transaction_read_only=on'), not as its own
                // top-level key, so this one checks the Options value's content rather than a key.
                // Spanner is included here deliberately: SpannerDialect inherits this unchanged
                // (the marker genuinely IS applied to its connection string) even though the real
                // Spanner database ignores it -- see ProductEnforcesConnectionStringReadOnly,
                // which is the gate that actually matters for whether a write gets rejected.
                return builder.TryGetValue("Options", out var pgOptions) &&
                       pgOptions is string pgOptionsText &&
                       pgOptionsText.Contains("default_transaction_read_only=on", StringComparison.OrdinalIgnoreCase);

            case SupportedDatabase.SqlServer:
                // SqlServerDialect.GetReadOnlyConnectionParameter() -> "ApplicationIntent=ReadOnly".
                // Detected here so a test can confirm the library applies it, but it is an
                // Availability Group routing hint, not server-side enforcement
                // (docs/supported-databases.md's Read-Only Enforcement Matrix) --
                // ProductEnforcesConnectionStringReadOnly returns false for SqlServer, so this
                // alone never makes IsReadOnly true.
                return builder.TryGetValue("ApplicationIntent", out var appIntent) &&
                       string.Equals(appIntent as string, "ReadOnly", StringComparison.OrdinalIgnoreCase);

            default:
                return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="product"/>'s real database actually refuses a write because of the
    /// connection-string marker <see cref="IsReadOnlyConnectionString"/> detects -- as opposed to
    /// the library merely having applied that marker. For most of the providers above these
    /// coincide (the marker is why the write fails), but two are documented exceptions
    /// (docs/supported-databases.md's Read-Only Enforcement Matrix, both live-confirmed):
    /// Spanner's database does not refuse a write in a read-intent transaction even though
    /// SpannerDialect (inheriting PostgreSqlDialect) applies the same connection-string marker
    /// PostgreSQL does; SQL Server's ApplicationIntent=ReadOnly is purely an Availability Group
    /// routing hint. fakeDbConnection.IsReadOnly ANDs this in alongside the marker detection so a
    /// test can still confirm the marker was applied (G7) independently of whether a write
    /// actually gets rejected (G7b) for these two.
    /// </summary>
    internal static bool ProductEnforcesConnectionStringReadOnly(SupportedDatabase product) =>
        product is not (SupportedDatabase.Spanner or SupportedDatabase.SqlServer);

    // MySqlDialect.SetSessionReadOnlySql/SetSessionReadWriteSql -- shared by MySql, AuroraMySql
    // (delegates to MySqlDialect unmodified) and SingleStore (inherits the same text unconditionally;
    // only EnforcesReadOnlyTransactions, read by nothing except MySqlDialect's own
    // SupportsReadOnlyTransactions getter, differs for it -- the SQL text itself still goes out).
    // MariaDbDialect overrides both with its own variable name (tx_read_only, not
    // transaction_read_only) -- genuinely different text, not a copy/paste choice, so it gets its
    // own case. TiDbDialect overrides ReadOnlyTransactionSql to null and ReadOnlySwitchStatement to
    // string.Empty: it sends no session SQL at all for BeginTransaction(ExecutionType.Read), so it
    // correctly matches neither method below.
    private const string MySqlReadOnlyEntrySql = "SET SESSION transaction_read_only = 1;";
    private const string MySqlReadOnlyResetSql = "SET SESSION transaction_read_only = 0;";
    private const string MariaDbReadOnlyEntrySql = "SET SESSION tx_read_only = 1;";
    private const string MariaDbReadOnlyResetSql = "SET SESSION tx_read_only = 0;";

    // Oracle/Informix/SAP HANA's shared ReadOnlyTransactionSql text (each dialect's own
    // SetTransactionReadOnlySql constant -- identical text, confirmed live for Oracle/Informix,
    // see each dialect's comments).
    private const string TransactionReadOnlyEntrySql = "SET TRANSACTION READ ONLY";

    // FirebirdDialect.GetReadOnlySessionSettings()/GetReadOnlyTransactionResetSql() -- sent as part
    // of DatabaseContext's combined per-pool session-settings command (baseline + intent in one
    // CommandText, e.g. "SET NAMES UTF8;\nSET SQL DIALECT 3;SET TRANSACTION READ ONLY;"), not as a
    // standalone statement like the other session-SQL providers above. That is why the match below
    // uses Contains, not an exact-string match: this text is always a substring of a larger batch
    // for Firebird, never the bare command on its own.
    private const string FirebirdReadOnlyEntrySql = "SET TRANSACTION READ ONLY;";
    private const string FirebirdReadOnlyResetSql = "SET TRANSACTION READ WRITE;";

    /// <summary>
    /// True when <paramref name="commandText"/> contains the exact SQL the real dialect sends to
    /// enter a read-only transaction/session for <paramref name="product"/> (G7b/CONCURRENCY.md's
    /// session-SQL providers, as opposed to <see cref="IsReadOnlyConnectionString"/>'s
    /// connection-string-marker providers). fakeDbCommand calls this to flip
    /// fakeDbConnection.SessionMarkedReadOnly on when it sees this text go by -- Contains rather
    /// than an exact match so it still finds Firebird's text inside its combined session-settings
    /// batch (see the comment above FirebirdReadOnlyEntrySql).
    /// </summary>
    internal static bool IsReadOnlySessionEntrySql(string? commandText, SupportedDatabase product)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        return product switch
        {
            SupportedDatabase.MySql or SupportedDatabase.AuroraMySql or SupportedDatabase.SingleStore =>
                commandText.Contains(MySqlReadOnlyEntrySql, StringComparison.OrdinalIgnoreCase),
            SupportedDatabase.MariaDb =>
                commandText.Contains(MariaDbReadOnlyEntrySql, StringComparison.OrdinalIgnoreCase),
            SupportedDatabase.Oracle or SupportedDatabase.Informix or SupportedDatabase.SapHana =>
                commandText.Contains(TransactionReadOnlyEntrySql, StringComparison.OrdinalIgnoreCase),
            SupportedDatabase.Firebird =>
                commandText.Contains(FirebirdReadOnlyEntrySql, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>
    /// True when <paramref name="commandText"/> contains the exact SQL the real dialect sends to
    /// reset a connection back to read-write after a read-only transaction (MySql/AuroraMySql/
    /// MariaDb/Firebird only -- Oracle, Informix, SAP HANA and SingleStore have no such text:
    /// Oracle/Informix/SAP HANA's read-only state is scoped to the transaction itself and ends at
    /// COMMIT/ROLLBACK rather than via a statement; SingleStore's server never actually enters a
    /// read-only state to reset in the first place, per <see cref="ProductEnforcesSessionReadOnly"/>).
    /// </summary>
    internal static bool IsReadOnlySessionResetSql(string? commandText, SupportedDatabase product)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        return product switch
        {
            SupportedDatabase.MySql or SupportedDatabase.AuroraMySql =>
                commandText.Contains(MySqlReadOnlyResetSql, StringComparison.OrdinalIgnoreCase),
            SupportedDatabase.MariaDb =>
                commandText.Contains(MariaDbReadOnlyResetSql, StringComparison.OrdinalIgnoreCase),
            SupportedDatabase.Firebird =>
                commandText.Contains(FirebirdReadOnlyResetSql, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>
    /// Whether <paramref name="product"/>'s real database actually refuses a write because of the
    /// session/transaction SQL <see cref="IsReadOnlySessionEntrySql"/> detects -- as opposed to the
    /// library merely having sent it. Two documented exceptions here
    /// (docs/read-only-enforcement.md's "Is a read-intent transaction read-only at the database?",
    /// live-verified 2026-10-03):
    ///  - SingleStore: its dialect sends MySQL's exact "SET SESSION transaction_read_only = 1;"
    ///    (MySqlDialect.ReadOnlyTransactionSql is unconditional; only EnforcesReadOnlyTransactions,
    ///    which nothing else reads, differs for it), but the server accepts the statement and
    ///    still allows the write.
    ///  - Firebird: explicitly listed under "No (the library's guard is the only protection)" in
    ///    that live-verified list, despite FirebirdDialect.TryClassifyProviderException being able
    ///    to classify error 335544361 (isc_read_only_trans) into ReadOnlyViolationException *if* a
    ///    real Firebird ever raised it. That classification capability existing is not the same
    ///    claim as this specific mechanism (the combined session-settings batch's
    ///    "SET TRANSACTION READ ONLY;" text) actually making a later write fail -- the live check
    ///    found it does not. (An earlier version of this method got this wrong by inferring
    ///    enforcement from the translator's classification code alone, without checking the
    ///    already-recorded live result in read-only-enforcement.md first.)
    /// Everything else that reaches <see cref="IsReadOnlySessionEntrySql"/> at all (MySql/
    /// AuroraMySql/MariaDb/Oracle/Informix/SAP HANA) genuinely enforces.
    /// </summary>
    internal static bool ProductEnforcesSessionReadOnly(SupportedDatabase product) =>
        product is not (SupportedDatabase.SingleStore or SupportedDatabase.Firebird);

    /// <summary>
    /// The leading DML write verb (for the provider error message), or null if this statement
    /// doesn't start with one. A minimal, deliberately narrow SQL-shape check used only by
    /// read-only enforcement: everything else in fakeDb stays SQL-agnostic by design (CLAUDE.md),
    /// but a real engine's read-only rejection fires based on what the statement actually is,
    /// regardless of which ADO.NET method ran it (ExecuteReader included — G6's whole point is
    /// that a write can be sent that way) — so without this, either every SELECT on a read-only
    /// connection would incorrectly fail, or ExecuteReader-shaped writes would incorrectly
    /// succeed. Not a general SQL parser and not used anywhere else.
    /// </summary>
    internal static string? WriteVerbOrNull(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return null;
        }

        var trimmed = commandText.AsSpan().TrimStart();
        foreach (var verb in WriteVerbs)
        {
            if (trimmed.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
            {
                return verb;
            }
        }

        return null;
    }

    private static readonly string[] WriteVerbs = { "INSERT", "UPDATE", "DELETE", "MERGE", "REPLACE" };
}
