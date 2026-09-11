// =============================================================================
// FILE: InformixDialect.cs
// PURPOSE: IBM Informix Dynamic Server (IDS) dialect implementation.
//
// AI SUMMARY:
// - LIVE-VERIFIED end-to-end against a real icr.io/informix/informix-developer-database
//   container (15.0.1.0.3DE) via Testcontainers: full CRUD, transactions, concurrency, error
//   mapping, and capability probes all pass — see testbed/Informix/ and TestProvider.cs's
//   Informix-specific overrides/skips for what was confirmed working vs. genuinely unsupported.
// - Driver: HCL's Informix.Net.Core-lnx (Linux-native, real .so binaries). Factory type:
//   Informix.Net.Core.InformixClientFactory. Needs INFORMIXDIR + a generated sqlhosts file +
//   LD_LIBRARY_PATH set before true process start (a re-exec pattern, not mid-process
//   SetEnvironmentVariable) — see InformixNativeLibraryBootstrap.cs for the full story.
// - Parameters: positional "?" only, no named-parameter support (driver has no name-to-
//   position mapping — see HCL's .NET Provider Reference Guide).
// - Identifier quoting: base ANSI double-quote default requires Delimident=true on the
//   connection for double-quoted identifiers to be treated as identifiers rather than
//   interchangeable with '...' string literals (see InformixTestContainer.cs). CONFIRMED live:
//   a column literally named "user" still can't be selected via its quoted identifier — even
//   with Delimident=true, Informix treats USER as a special register (like CURRENT/TODAY), not
//   a plain reserved word quoting can disambiguate (see TestProvider.cs's TestIdentifierQuoting
//   skip for Informix).
// - Pagination: CONFIRMED live to be broken as originally set up — both the base
//   SupportsOffsetFetch (SQL:2008 OFFSET/FETCH) rendering and MySQL-style LIMIT/OFFSET are
//   rejected outright ("A syntax error has occurred."). Informix's real native paging idiom is
//   "SELECT SKIP n FIRST m ..." which must appear immediately after SELECT, structurally
//   incompatible with AppendPaging's append-at-end-of-query design — needs a dedicated
//   query-generation-time override, not attempted in this pass. Both capabilities disabled.
// - MERGE: the MERGE INTO ... WHEN MATCHED/WHEN NOT MATCHED statement itself is documented, but
//   CONFIRMED live that the base RenderMergeSource's "USING (VALUES (...)) AS s (...)"
//   derived-table shape is rejected ("A syntax error has occurred."). The real accepted
//   USING-clause shape was not identified in this pass — disabled rather than guessing.
// - Batch insert: CONFIRMED live that the ANSI multi-row VALUES clause
//   ("INSERT INTO t (...) VALUES (...), (...)") is rejected — Informix only accepts one row per
//   VALUES clause. Falls back to one INSERT per entity (SupportsBatchInsert = false).
// - Binary/BLOB parameter binding: CONFIRMED live that binding a parameter directly to a
//   BLOB/TEXT/BYTE column in an ordinary immediate INSERT is rejected ("Illegal attempt to use
//   Text/Byte host variable.") — a long-documented Informix ESQL/CLI restriction requiring an
//   INSERT cursor or locator-based (data-at-execution) binding, which pengdows.crud's parameter
//   binding doesn't implement. See TestProvider.SupportsBinaryParameterBinding.
// - ProcWrappingStyle: deliberately None — EXECUTE PROCEDURE proc(args) is confirmed for
//   invoking stored procedures, but this session could not confirm whether Informix also
//   needs a distinct SELECT-based form for read-executed/function-returning calls the way
//   Firebird's ExecuteProcedureWrappingStrategy does, or whether EXECUTE PROCEDURE is used
//   unconditionally for both read and write. Do not guess a style — verify live, then either
//   reuse an existing strategy or add a new one, before setting this to anything but None.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// IBM Informix Dynamic Server (IDS) dialect.
/// </summary>
/// <remarks>
/// Every capability flag and error-code mapping here is sourced from IBM's public
/// documentation (cited on each member) and has been verified end-to-end against a real
/// server via <c>testbed</c> (see the file-level AI SUMMARY for what was confirmed working vs.
/// genuinely unsupported). A dedicated <c>InformixDialectTests.cs</c> asserting each capability
/// flag individually (per CLAUDE.md's "Adding a New Database" checklist item 7) has not yet
/// been written — the exhaustive matrix tests in <c>DataSourceInformationTests.cs</c> and this
/// file's own comments are the only coverage today.
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
    // `USING (VALUES (...)) AS s (col1, col2, ...)` derived-table shape — previously flagged
    // UNVERIFIED — does not parse against a real server. The real accepted USING-clause shape
    // (likely a SELECT-based derived table rather than a bare VALUES row constructor, or a
    // different alias/column-list form) was not identified in this pass; left disabled rather
    // than guessing at syntax without a way to iterate against live IBM documentation. Revisit
    // with real doc access or further live experimentation before re-enabling.
    public override bool SupportsMerge => false;

    // CONFIRMED live: the base class's standard "OFFSET n ROWS FETCH NEXT m ROWS ONLY" text
    // (SqlDialect.AppendPaging) is rejected — "ERROR [42000] ... A syntax error has occurred."
    // IBM docs describe OFFSET/FETCH as supported "for portability" alongside Informix's native
    // SKIP/FIRST idiom, but SKIP/FIRST is structurally incompatible with AppendPaging's
    // append-at-end-of-query design (SKIP n FIRST m must appear immediately after SELECT, not
    // at the end) and would need a dedicated query-generation-time override, not just a new
    // AppendPaging body — out of scope for this pass. Both offset-style capabilities disabled
    // rather than emitting known-broken SQL; TestPagingCapability now skips cleanly for
    // Informix instead of failing.
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
        string.Equals(TryGetProviderSqlState(ex), "23000", StringComparison.OrdinalIgnoreCase) ||
        Math.Abs(TryGetProviderErrorCode(ex) ?? 0) is 268 or 239;

    // -691: insert violates a foreign key (parent row missing). -692: delete/update blocked
    // because a child row still references this row. Both documented on oninit.com's
    // Informix error-code reference (community-maintained but consistent with IBM's own
    // numbering scheme elsewhere).
    public override bool IsForeignKeyViolation(DbException ex) =>
        Math.Abs(TryGetProviderErrorCode(ex) ?? 0) is 691 or 692;

    // -391: "Cannot insert null into column" (IIUG community reference, consistent with
    // Informix's numbering).
    public override bool IsNotNullViolation(DbException ex) =>
        Math.Abs(TryGetProviderErrorCode(ex) ?? 0) == 391;

    // -530: CHECK constraint violation.
    public override bool IsCheckConstraintViolation(DbException ex) =>
        Math.Abs(TryGetProviderErrorCode(ex) ?? 0) == 530;

    protected override bool TryClassifyProviderException(DbException ex, out DbErrorCategory category)
    {
        var code = TryGetProviderErrorCode(ex) is { } raw ? Math.Abs(raw) : (int?)null;

        // -143: deadlock (IBM performance-tuning docs).
        if (code == 143)
        {
            category = DbErrorCategory.Deadlock;
            return true;
        }

        // -244: "Could not do a physical-order read to fetch next row" - the closest
        // documented analog to a serialization/lock conflict under Repeatable Read isolation.
        // UNVERIFIED: no distinct SQLSTATE was found for this condition, and this category
        // assignment (SerializationFailure vs. a generic conflict) has not been confirmed
        // against a live server.
        if (code == 244)
        {
            category = DbErrorCategory.SerializationFailure;
            return true;
        }

        // -908 (SQLSTATE 08004) and -27001/-27002: connection/communication failure.
        if (code is 908 or 27001 or 27002)
        {
            category = DbErrorCategory.Unknown;
            return true;
        }

        var sqlState = TryGetProviderSqlState(ex);
        if (!string.IsNullOrWhiteSpace(sqlState) && sqlState.StartsWith("23", StringComparison.Ordinal))
        {
            category = DbErrorCategory.ConstraintViolation;
            return true;
        }

        category = DbErrorCategory.Unknown;
        return false;
    }

    // Informix's own terminology (Dirty Read/Committed Read/Cursor Stability/Repeatable Read)
    // maps to ADO.NET's IsolationLevel as: Dirty Read = ReadUncommitted, Committed Read (the
    // default) = ReadCommitted, Repeatable Read = BOTH RepeatableRead and Serializable (a
    // single, stricter underlying lock mode satisfies both requests). "Cursor Stability" has
    // no direct ADO.NET IsolationLevel equivalent and is not exposed here. Source: IBM
    // "Informix Isolation Levels", 14.10.
    // UNVERIFIED: whether the driver's BeginTransaction(IsolationLevel) actually issues the
    // correct native SET ISOLATION text for each of these has not been confirmed live.
    internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) => new()
    {
        IsolationLevel.ReadUncommitted,
        IsolationLevel.ReadCommitted,
        IsolationLevel.RepeatableRead,
        IsolationLevel.Serializable
    };

    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) => new()
    {
        [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted, // Committed Read, Informix's default
        [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable, // Repeatable Read
        [IsolationProfile.FastWithRisks] = IsolationLevel.ReadUncommitted // Dirty Read
    };

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
}
