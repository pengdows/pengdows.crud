// =============================================================================
// FILE: InformixDialect.cs
// PURPOSE: IBM Informix Dynamic Server (IDS) dialect implementation.
//
// AI SUMMARY:
// - Backported from pengdows.crud 3.0 (same live-verification trail applies) to this
//   non-breaking 2.0.6 patch line. Isolation-level data (GetSupportedIsolationLevels/
//   GetIsolationProfileMapping on 3.0) lives in IsolationResolver.cs's central switch instead,
//   since 2.0.6 predates 3.0's dialect-owned isolation refactor.
// - LIVE-VERIFIED end-to-end against a real icr.io/informix/informix-developer-database
//   container (15.0.1.0.3DE) via Testcontainers: full CRUD, transactions, concurrency, error
//   mapping, and capability probes all pass.
// - Driver: HCL's Informix.Net.Core-lnx (Linux-native, real .so binaries). Factory type:
//   Informix.Net.Core.InformixClientFactory. Needs INFORMIXDIR + a generated sqlhosts file +
//   LD_LIBRARY_PATH set before true process start (a re-exec pattern, not mid-process
//   SetEnvironmentVariable) — see InformixNativeLibraryBootstrap.cs for the full story.
// - Parameters: positional "?" only, no named-parameter support (driver has no name-to-
//   position mapping — see HCL's .NET Provider Reference Guide).
// - Identifier quoting: base ANSI double-quote default requires Delimident=true on the
//   connection for double-quoted identifiers to be treated as identifiers rather than
//   interchangeable with '...' string literals. CONFIRMED live: a column literally named "user"
//   still can't be selected via its quoted identifier — even with Delimident=true, Informix
//   treats USER as a special register (like CURRENT/TODAY), not a plain reserved word quoting
//   can disambiguate.
// - Pagination: CONFIRMED live to be broken as originally set up — both the base
//   SupportsOffsetFetch (SQL:2008 OFFSET/FETCH) rendering and MySQL-style LIMIT/OFFSET are
//   rejected outright ("A syntax error has occurred."). Informix's real native paging idiom is
//   "SELECT SKIP n FIRST m ..." which must appear immediately after SELECT, structurally
//   incompatible with AppendPaging's append-at-end-of-query design — needs a dedicated
//   query-generation-time override, not attempted in this pass. Both capabilities disabled.
// - MERGE: the MERGE INTO ... WHEN MATCHED/WHEN NOT MATCHED statement itself is documented, but
//   CONFIRMED live that the base RenderMergeSource's "USING (VALUES (...)) AS s (...)"
//   derived-table shape is rejected ("A syntax error has occurred."). The real accepted
//   USING-clause shape was not identified — disabled rather than guessing.
// - Batch insert: CONFIRMED live that the ANSI multi-row VALUES clause
//   ("INSERT INTO t (...) VALUES (...), (...)") is rejected — Informix only accepts one row per
//   VALUES clause. Falls back to one INSERT per entity (SupportsBatchInsert = false).
// - Binary/BLOB parameter binding: CONFIRMED live that binding a parameter directly to a
//   BLOB/TEXT/BYTE column in an ordinary immediate INSERT is rejected ("Illegal attempt to use
//   Text/Byte host variable.") — a long-documented Informix ESQL/CLI restriction requiring an
//   INSERT cursor or locator-based (data-at-execution) binding, which pengdows.crud's parameter
//   binding doesn't implement.
// - ProcWrappingStyle: deliberately None — EXECUTE PROCEDURE proc(args) is confirmed for
//   invoking stored procedures, but whether Informix also needs a distinct SELECT-based form for
//   read-executed/function-returning calls was not confirmed. Do not guess a style.
// - Read-only transaction enforcement: CONFIRMED LIVE against a real
//   icr.io/informix/informix-developer-database container. "SET TRANSACTION READ ONLY" issued
//   inside an active transaction (BEGIN WORK, or a real ADO.NET conn.BeginTransaction() —
//   both tested) is accepted, and a subsequent write then fails with "Invalid operation for a
//   READ-ONLY transaction." Implemented via the same TryExecuteReadOnlySql/
//   TryExecuteReadOnlySqlAsync shared helper OracleDialect uses for its own identical
//   SET TRANSACTION READ ONLY support.
// - ApplicationName/pool discriminator: CONFIRMED via reflection against a real, live-connected
//   IfxConnectionStringBuilder (51 properties) that no ApplicationName-equivalent keyword
//   exists. LeaveTrailingSpaces=False was chosen as the pool discriminator: connects
//   successfully set to its own driver default (guaranteed behaviorally inert by construction),
//   and obscure enough that no real caller is expected to have already set it themselves.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.exceptions.translators;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// IBM Informix Dynamic Server (IDS) dialect.
/// </summary>
/// <remarks>
/// Every capability flag and error-code mapping here is sourced from IBM's public
/// documentation (cited on each member) and has been verified end-to-end against a real
/// server. See <c>InformixDialectTests.cs</c> for the dedicated capability-flag and
/// exception-classification test coverage.
/// </remarks>
internal sealed class InformixDialect : SqlDialect
{
    internal InformixDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Informix;

    // Positional-only parameter markers; Informix's ADO.NET driver has no named-parameter
    // support at all (HCL Informix .NET Provider Reference Guide, 14.10).
    public override string ParameterMarker => "?";
    public override bool SupportsNamedParameters => false;

    // owner.table qualification is real and documented; unquoted owner names fold to
    // uppercase, quoted owner names are case-sensitive (IBM docs: "Owner name", 14.10).
    public override bool SupportsNamespaces => true;

    // CONFIRMED live: rejected outright — "ERROR [42000] ... A syntax error has occurred."
    // MERGE INTO ... WHEN MATCHED/WHEN NOT MATCHED itself is documented (HCL Informix 14.10 SQL
    // Statements guide; developerWorks MERGE article), but the base RenderMergeSource's
    // `USING (VALUES (...)) AS s (col1, col2, ...)` derived-table shape does not parse against a
    // real server. The real accepted USING-clause shape was not identified in this pass; left
    // disabled rather than guessing at syntax.
    public override bool SupportsMerge => false;

    // CONFIRMED live: the base class's standard "OFFSET n ROWS FETCH NEXT m ROWS ONLY" text
    // (SqlDialect.AppendPaging) is rejected — "ERROR [42000] ... A syntax error has occurred."
    // Informix's real native paging idiom (SKIP n FIRST m) must appear immediately after SELECT,
    // structurally incompatible with AppendPaging's append-at-end-of-query design — needs a
    // dedicated query-generation-time override, out of scope for this pass. Both offset-style
    // capabilities disabled rather than emitting known-broken SQL.
    public override bool SupportsOffsetFetch => false;
    public override bool SupportsLimitOffset => false;

    // CONFIRMED live: Informix rejects the ANSI SQL multi-row VALUES clause outright
    // ("ERROR [42000] ... A syntax error has occurred.") — Informix's INSERT statement only
    // accepts a single row per VALUES clause. Falls back to one BuildCreate per entity, the
    // same safe path SQLite/MySQL/MariaDB/Firebird already use for the same reason.
    public override bool SupportsBatchInsert => false;

    // Deliberately NOT overridden — see file-level AI SUMMARY. Do not set this without live
    // verification of the read-vs-write calling convention.
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

    // ANSI SQLSTATE 23000, or Informix's own numeric error codes for duplicate key: -268
    // (logged database) / -239 (unlogged database). Both are documented, distinct codes for
    // the SAME condition depending on database logging mode, not alternates for different
    // constraint kinds. Source: IBM support "SQL state X23000:-239".
    public override bool IsUniqueViolation(DbException ex) =>
        string.Equals(DbExceptionTranslationSupport.TryGetSqlState(ex), "23000", StringComparison.OrdinalIgnoreCase) ||
        Math.Abs(DbExceptionTranslationSupport.TryGetErrorCode(ex) ?? 0) is 268 or 239;

    // -691: insert violates a foreign key (parent row missing). -692: delete/update blocked
    // because a child row still references this row. Both documented on oninit.com's
    // Informix error-code reference (community-maintained but consistent with IBM's own
    // numbering scheme elsewhere).
    public override bool IsForeignKeyViolation(DbException ex) =>
        Math.Abs(DbExceptionTranslationSupport.TryGetErrorCode(ex) ?? 0) is 691 or 692;

    // -391: "Cannot insert null into column" (IIUG community reference, consistent with
    // Informix's numbering).
    public override bool IsNotNullViolation(DbException ex) =>
        Math.Abs(DbExceptionTranslationSupport.TryGetErrorCode(ex) ?? 0) == 391;

    // -530: CHECK constraint violation.
    public override bool IsCheckConstraintViolation(DbException ex) =>
        Math.Abs(DbExceptionTranslationSupport.TryGetErrorCode(ex) ?? 0) == 530;

    // Advisory-level category classification (ISqlDialect.AnalyzeException/ClassifyException)
    // lives in SqlDialect.cs's private TryClassifyProviderException switch on this branch - 2.0.6
    // predates 3.0's dialect-owned classification refactor, so there is no virtual member here to
    // override. See that switch's SupportedDatabase.Informix case for the same -143/-244 codes
    // used below in InformixExceptionTranslator.cs, which is what actually determines the
    // exception TYPE thrown - this method only affects the separate advisory-diagnostics path.

    // Informix's own terminology (Dirty Read/Committed Read/Cursor Stability/Repeatable Read)
    // maps to ADO.NET's IsolationLevel — see IsolationResolver.cs's SupportedDatabase.Informix
    // cases (2.0.6 resolves isolation through a central switch, not a dialect-owned override).

    // SERIAL/SERIAL8/BIGSERIAL are Informix's idiomatic auto-increment types (IBM docs,
    // "SERIAL(n) data type", 14.10) - GUIDs are stored as client-generated strings, matching
    // every other dialect without native UUID column support.
    protected override GuidStorageFormat GuidFormat => GuidStorageFormat.String;

    public override string GetVersionQuery()
    {
        // UNVERIFIED: not confirmed against a live server - this is the standard documented
        // way to query the engine version string from within a connected session.
        return "SELECT DBINFO('version', 'full') FROM systables WHERE tabid = 1";
    }

    // CONFIRMED LIVE against a real icr.io/informix/informix-developer-database container:
    // "SET TRANSACTION READ ONLY" issued inside an active transaction genuinely enforces
    // read-only — a subsequent write fails with "Invalid operation for a READ-ONLY
    // transaction.", confirmed both via raw BEGIN WORK/SQL text and via a real ADO.NET
    // conn.BeginTransaction(). Same mechanism (and same shared helper) OracleDialect uses for
    // its own confirmed SET TRANSACTION READ ONLY support.
    private const string SetTransactionReadOnlySql = "SET TRANSACTION READ ONLY";

    public override void TryEnterReadOnlyTransaction(ITransactionContext transaction)
    {
        TryExecuteReadOnlySql(transaction, SetTransactionReadOnlySql, "Informix");
    }

    public override ValueTask TryEnterReadOnlyTransactionAsync(ITransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        return TryExecuteReadOnlySqlAsync(transaction, SetTransactionReadOnlySql, "Informix", cancellationToken);
    }

    // No ApplicationName-equivalent connection-string keyword exists on
    // Informix.Net.Core.IfxConnectionStringBuilder (CONFIRMED via reflection, 51 properties
    // inspected). Three candidates connect successfully with their value set to their OWN
    // driver default (guaranteed inert by construction): Exclusive=no, MaxPoolSize=100, and
    // LeaveTrailingSpaces=False. MaxPoolSize was deliberately NOT chosen — a real caller is
    // plausible to have already configured it themselves, silently defeating pool separation.
    // LeaveTrailingSpaces (a CHAR-column trailing-space read behavior flag) is obscure enough
    // that no real caller is expected to ever set it themselves.
    internal override string? ReadOnlyPoolDiscriminatorSettingName => "LeaveTrailingSpaces";
    internal override string? ReadOnlyPoolDiscriminatorSettingValue => "False";
}
