// =============================================================================
// FILE: FlatFileDialect.cs
// PURPOSE: Dialect for pengdows.flatfile — a file-backed ADO.NET provider over
//          CSV/TSV/pipe/delimited/fixed-width/NDJSON files.
//
// STATUS: Partial. Only the properties below reflect a deliberate, verified decision
// against pengdows.flatfile's actual behavior (see citations on each). Everything not
// overridden here still falls through to SqlDialect's generic defaults and has NOT been
// verified against pengdows.flatfile — in particular generated-key/identity plan (flatfile
// has no autoincrement/sequence/RETURNING concept at all) and session settings. Decide
// those from real research against pengdows.flatfile's source, not by copying another
// embedded dialect's assumptions — see CLAUDE.md's "Adding a New Database" checklist and
// its SAP HANA callout for the same caution.
//
// Isolation levels/profiles ARE now decided (see GetSupportedIsolationLevels/
// GetIsolationProfileMapping below) — this used to say the opposite ("NOT verified... its
// FlatFileTransaction is file-snapshot/undo-journal rollback, explicitly not real
// concurrent-connection isolation or MVCC"), which was true of the ENGINE at the time that
// was written but is no longer true: pengdows.flatfile's FlatFileTransaction now takes a
// real per-table snapshot on first read under RepeatableRead/Serializable/Snapshot,
// confirmed live (a Serializable reader's second read of a table no longer picks up a
// concurrent writer's commit — see pengdows.flatfile's TransactionIsolationTests.cs). The
// underlying write-aside DML staging (real file untouched until Commit) already prevented
// dirty reads unconditionally; what changed is repeatable-read/phantom-read protection for
// RepeatableRead/Serializable/Snapshot specifically.
//
// CoerceConnectionMode/DbMode-Best selection IS decided (see IsEmbeddedSingleWriterEngine/
// CoerceConnectionMode below): pengdows.flatfile has exactly one writer lock per
// directory/file (FlatFileConnection.Open() / ConnectionWriteLock), the same real
// constraint SQLite/DuckDB have, so it reuses SqlDialect's shared
// CoerceEmbeddedSingleWriterMode policy — Best resolves to SingleWriter. Confirmed live
// via pengdows.crud.IntegrationTests.ConnectionManagement.DbModeTests: forcing this
// through an explicit DbMode.SingleWriter override at the test-container level (the
// prior workaround, since removed) produced identical behavior to letting Best resolve
// it, which is exactly what "this dialect decides it now" should look like. FlatFile has
// no `:memory:` concept at all (always file/directory-backed), so DetectInMemoryKind is
// NOT overridden — the base SqlDialect default (always None) is already correct.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Dialect for <c>pengdows.flatfile</c>. See the file-level STATUS remark above — this is a
/// deliberately partial dialect, not a finished one.
/// </summary>
internal class FlatFileDialect : SqlDialect
{
    internal FlatFileDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.FlatFile;

    /// <summary>
    /// pengdows.flatfile supports named parameters via <c>:name</c> — the ISO SQL dynamic-SQL
    /// host-variable form (see pengdows.flatfile/SQL_STANDARDS_STATUS.md and
    /// pengdows.sql/SqlParser.cs). Its README previously claimed positional-only support with no
    /// named parameters at all; that claim was stale/incorrect and has been corrected there too —
    /// verified directly against the engine's parser/binder, not assumed from documentation.
    /// <c>@name</c> (T-SQL) and <c>$1</c>/<c>$2</c> (PostgreSQL) are deliberately NOT supported
    /// (rejected unconditionally as vendor dialect), so <c>:name</c> is the only named form.
    /// </summary>
    public override bool SupportsNamedParameters => true;

    /// <summary>
    /// pengdows.flatfile's named-parameter form is <c>:name</c> (see <see cref="SupportsNamedParameters"/>
    /// above) — same marker character as <see cref="OracleDialect"/>.
    /// </summary>
    public override string ParameterMarker => ":";

    /// <summary>
    /// pengdows.flatfile has no stored-procedure/trigger/control-flow support at all (confirmed:
    /// its README lists this under "Not supported"). <see cref="ProcWrappingStyle.None"/> is the
    /// base default already, but this override documents that the value was verified, not left
    /// unexamined — see CLAUDE.md checklist item 12 on why an unexamined <c>None</c> is dangerous.
    /// </summary>
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

    /// <summary>
    /// pengdows.flatfile is an embedded, per-directory file engine with no server process — the
    /// same classification as SQLite/DuckDB, not a client-server RDBMS.
    /// </summary>
    public override bool IsClientServerDatabase => false;

    /// <summary>
    /// pengdows.flatfile allows exactly one non-readonly connection per directory/file at a time —
    /// <see cref="FlatFileConnection.Open"/> acquires a real exclusive write lock
    /// (<c>ConnectionWriteLock</c>), the same single-writer constraint SQLite/DuckDB have (see
    /// <c>WriteConnectionEnforcementTests</c> in pengdows.flatfile.Tests). Same classification as
    /// those two, not a guess.
    /// </summary>
    public override bool IsEmbeddedSingleWriterEngine => true;

    /// <summary>
    /// Confirmed live against pengdows.flatfile (this session — see its
    /// TransactionIsolationTests.cs): all four standard levels are genuinely meaningful, not
    /// just accepted without differentiation.
    /// <list type="bullet">
    ///   <item><see cref="IsolationLevel.ReadUncommitted"/>/<see cref="IsolationLevel.ReadCommitted"/> —
    ///   live reads of the real file. DML is always staged (write-aside: the real file is
    ///   untouched until Commit), so dirty reads are structurally impossible even for
    ///   ReadUncommitted — the same "can't actually deliver a weaker guarantee than what's
    ///   requested" over-delivery real engines like PostgreSQL exhibit for their own
    ///   READ UNCOMMITTED.</item>
    ///   <item><see cref="IsolationLevel.RepeatableRead"/>/<see cref="IsolationLevel.Serializable"/>/
    ///   <see cref="IsolationLevel.Snapshot"/> — a table's file is copied into the transaction's
    ///   own journal on first read, and every later read of that table within the same
    ///   transaction resolves to that frozen copy (see FlatFileTransaction.ResolveForRead's
    ///   RequiresSnapshotRead branch). Because the snapshot is the WHOLE file rather than
    ///   per-row tracking, this prevents phantom reads too, not just non-repeatable reads — so
    ///   RepeatableRead and Serializable deliver identical, genuinely Serializable-strength
    ///   guarantees here. This is sufficient for real serializability (not just
    ///   snapshot-isolation-with-write-skew-risk) specifically BECAUSE
    ///   <see cref="IsEmbeddedSingleWriterEngine"/>'s ConnectionWriteLock already guarantees at
    ///   most one writer transaction exists at a time — there is no second concurrent writer a
    ///   snapshot reader could ever need to serialize against.</item>
    /// </list>
    /// </summary>
    internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) => new()
    {
        IsolationLevel.ReadUncommitted,
        IsolationLevel.ReadCommitted,
        IsolationLevel.RepeatableRead,
        IsolationLevel.Serializable
    };

    /// <summary>
    /// FastWithRisks maps to ReadCommitted rather than ReadUncommitted: both behave identically
    /// on this engine (dirty reads are impossible either way — see
    /// <see cref="GetSupportedIsolationLevels"/>), so ReadCommitted is the more honest name for
    /// what a caller actually gets when asking for the "fast, accept some risk" profile.
    /// </summary>
    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) => new()
    {
        [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.RepeatableRead,
        [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
        [IsolationProfile.FastWithRisks] = IsolationLevel.ReadCommitted
    };

    /// <summary>
    /// Reuses <see cref="SqlDialect.CoerceEmbeddedSingleWriterMode"/> — the exact same policy
    /// SqliteDialect/DuckDbDialect use — since the underlying constraint (one writer at a time,
    /// safest under Best) is identical. <see cref="DetectInMemoryKind"/> is not overridden (base
    /// default: always <see cref="InMemoryKind.None"/>), since pengdows.flatfile has no
    /// <c>:memory:</c> concept — every connection is a real directory or file.
    /// </summary>
    public override (DbMode Mode, string Reason) CoerceConnectionMode(DbMode requested, string? connectionString,
        bool isLocalDb) =>
        CoerceEmbeddedSingleWriterMode(requested, DetectInMemoryKind(connectionString));

    /// <summary>
    /// pengdows.flatfile's <c>FlatFileException.SqlState</c> deliberately uses the real ANSI SQL
    /// class-23 codes a server-based database would report (see pengdows.flatfile's own CLAUDE.md
    /// "Constraint Violations" section and <c>FlatFileException.cs</c>'s file-level remarks) —
    /// exactly so a consumer like this dialect can classify it via SqlState alone, the same pattern
    /// PostgreSqlDialect already uses for its own identical code set. The base
    /// SqlDialect.IsXxxViolation overrides only match message text ("foreign key", "not null",
    /// etc.), which FlatFileException's actual messages don't contain, so without these overrides
    /// every FlatFile constraint violation fell through to a generic, unclassified exception.
    /// </summary>
    public override bool IsUniqueViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23505", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc cref="IsUniqueViolation"/>
    public override bool IsForeignKeyViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23503", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc cref="IsUniqueViolation"/>
    public override bool IsNotNullViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23502", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc cref="IsUniqueViolation"/>
    public override bool IsCheckConstraintViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23514", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Verified: pengdows.flatfile's SQL parser (<c>pengdows.sql/SqlParser.cs</c>) genuinely
    /// parses an <c>IF EXISTS</c> clause on <c>DROP TABLE</c>/<c>DROP VIEW</c>/<c>DROP INDEX</c>
    /// (see its <c>SqlAst.cs</c> <c>IfExists</c> properties), so the base <c>true</c> default is
    /// confirmed correct here rather than inherited blind.
    /// </summary>
    public override bool SupportsDropTableIfExists => true;

    /// <summary>
    /// <c>SELECT version()</c> — the base <see cref="SqlDialect.GetDatabaseVersionAsync"/> fallback
    /// when a dialect doesn't override <see cref="GetVersionQuery"/> — is not a SQL-standard
    /// function; it happens to work for Postgres-family and DuckDB engines because they each
    /// implement it as a real builtin (see <see cref="DuckDbDialect.GetVersionQuery"/>), not because
    /// any standard guarantees it. pengdows.flatfile's SQL grammar has no <c>version()</c> function
    /// at all, so that guess would simply fail against a real flatfile connection. Its ADO.NET
    /// provider instead exposes version the idiomatic ADO.NET way — <c>FlatFileConnection.ServerVersion</c>
    /// (currently hardcoded to <c>"1.0"</c>) — so read that directly instead of executing SQL.
    /// </summary>
    public override Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        return Task.FromResult(connection.ServerVersion);
    }

    /// <summary>
    /// pengdows.flatfile's <c>ServerVersion</c> is hardcoded <c>"1.0"</c> — there is only one
    /// Verified features supported by pengdows.flatfile (see pengdows.sql/SqlParser.cs,
    /// SqlAst.cs's SqlWindowFunction / SqlWindowFrameClause, and BoundPredicateEvaluator's
    /// JsonValue handling): MERGE INTO, CTEs (<c>WITH</c>/<c>WITH RECURSIVE</c>), window functions
    /// including <c>NTH_VALUE</c> and full frame-clause support (SQL:2011 "enhanced" window
    /// functions), <c>TRUNCATE TABLE</c>, and <c>JSON_VALUE</c>/<c>JSON_TABLE</c> (SQL:2016).
    /// </summary>
    public override bool SupportsMerge => true;

    public override bool SupportsWindowFunctions => true;

    public override bool SupportsEnhancedWindowFunctions => true;

    public override bool SupportsCommonTableExpressions => true;

    public override bool SupportsJsonTypes => true;

    /// <summary>
    /// pengdows.flatfile has no <c>CREATE TYPE</c>/user-defined type support at all — verified: no
    /// false rather than being silently (and incorrectly) implied true.
    /// </summary>
    public override bool SupportsUserDefinedTypes => false;

    /// <summary>
    /// No ARRAY type exists in pengdows.flatfile's type system (see pengdows.flatfile/CLAUDE.md's
    /// "Type System" section listing its 17 supported CLR types — no array/collection type among
    /// them). See <see cref="SupportsUserDefinedTypes"/> for why this Sql99-tier flag needs an
    /// explicit override.
    /// </summary>
    public override bool SupportsArrayTypes => false;

    /// <summary>
    /// No regular-expression predicate exists (no <c>SIMILAR TO</c>, no <c>REGEXP</c> — verified:
    /// neither token appears anywhere in pengdows.sql's lexer/parser). See
    /// <see cref="SupportsUserDefinedTypes"/> for why this Sql99-tier flag needs an explicit
    /// override.
    /// </summary>
    public override bool SupportsRegularExpressions => false;

    /// <summary>
    /// No XML type or XML functions exist anywhere in pengdows.flatfile — it is a CSV/TSV/pipe/
    /// fixed-width/NDJSON engine with no XML format or type support at all. See
    /// <see cref="SupportsUserDefinedTypes"/> for why this Sql2003-tier flag needs an explicit
    /// override even though other Sql2003 features (MERGE, CTEs, window functions) are real.
    /// </summary>
    public override bool SupportsXmlTypes => false;

    /// <summary>
    /// pengdows.flatfile has no trigger support of any kind (see <see cref="ProcWrappingStyle"/>'s
    /// remarks — no stored-procedure/trigger/control-flow support at all, confirmed against its
    /// own README). See <see cref="SupportsUserDefinedTypes"/> for why this Sql2008-tier flag
    /// needs an explicit override.
    /// </summary>
    public override bool SupportsInsteadOfTriggers => false;

    /// <summary>
    /// Temporal table versioning (<c>FOR SYSTEM_TIME AS OF ...</c>) is explicitly listed as an
    /// unimplemented gap in pengdows.flatfile/SQL_STANDARDS_STATUS.md's "Remaining Standards Gaps"
    /// section. See <see cref="SupportsUserDefinedTypes"/> for why this Sql2011-tier flag needs an
    /// explicit override even though other Sql2011 features (NTH_VALUE, frame clauses) are real.
    /// </summary>
    public override bool SupportsTemporalData => false;

    /// <summary>
    /// No <c>MATCH_RECOGNIZE</c> row pattern matching exists anywhere in pengdows.flatfile -
    /// verified: the token/keyword appears nowhere in pengdows.sql's grammar. See
    /// <see cref="SupportsUserDefinedTypes"/> for why this Sql2016-tier flag needs an explicit
    /// override even though the other Sql2016 feature this dialect claims (JSON_VALUE/JSON_TABLE)
    /// is real.
    /// </summary>
    public override bool SupportsRowPatternMatching => false;

    /// <summary>
    /// Verified against pengdows.sql/SqlParser.cs's ParseMerge: the <c>WHEN MATCHED THEN UPDATE
    /// SET</c> clause parses a bare unqualified column name only (ExpectIdentifier then
    /// Expect(Equals)) — a target-aliased <c>t.column = ...</c> assignment fails to parse
    /// ("Expected token 'Equals' ... but found 'Dot'."). Same divergence as PostgreSQL/DuckDB (see
    /// their own overrides of this same property), unlike the <see cref="SqlDialect"/> base
    /// default of <c>true</c> (written for SQL Server/Oracle-style MERGE).
    /// </summary>
    public override bool MergeUpdateRequiresTargetAlias => false;

    /// <summary>
    /// pengdows.flatfile's <c>ClrTypeParser</c> has no <see cref="Guid"/> support at all among its
    /// 17 supported CLR types (see pengdows.flatfile/CLAUDE.md's "Type System" section) — a Guid
    /// column must round-trip as a plain string (declared <c>VARCHAR(36)</c> in DDL), same as
    /// Oracle/Spanner/Db2's approach for the same underlying reason.
    /// </summary>
    protected override GuidStorageFormat GuidFormat => GuidStorageFormat.String;
}
