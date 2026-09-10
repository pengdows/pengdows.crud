// =============================================================================
// FILE: FlatFileDialect.cs
// PURPOSE: Dialect for pengdows.flatfile — a file-backed ADO.NET provider over
//          CSV/TSV/pipe/delimited/fixed-width/NDJSON files.
//
// STATUS: Partial. Only the properties below reflect a deliberate, verified decision
// against pengdows.flatfile's actual behavior (see citations on each). Everything not
// overridden here still falls through to SqlDialect's generic defaults and has NOT been
// verified against pengdows.flatfile — in particular isolation levels/profiles (its
// FlatFileTransaction is file-snapshot/undo-journal rollback, explicitly not real
// concurrent-connection isolation or MVCC per its own README), generated-key/identity
// plan (flatfile has no autoincrement/sequence/RETURNING concept at all), session
// settings, and CoerceConnectionMode/DbMode-Best selection. Decide those from real
// research against pengdows.flatfile's source, not by copying another embedded dialect's
// assumptions — see CLAUDE.md's "Adding a New Database" checklist and its SAP HANA
// callout for the same caution.
// =============================================================================

using System;
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
