// =============================================================================
// FILE: AccessDialect.cs
// PURPOSE: Microsoft Access (Jet/ACE) dialect implementation.
//
// AI SUMMARY:
// - No native ADO.NET Jet/ACE client exists — this dialect targets
//   System.Data.OleDb.OleDbFactory/OleDbConnection with
//   Provider=Microsoft.ACE.OLEDB.16.0 in the connection string.
// - LIVE-VERIFIED this session against a real .accdb file, using BOTH the older
//   Microsoft.ACE.OLEDB.12.0 (~2010) and current Microsoft.ACE.OLEDB.16.0 providers — both
//   behave identically for everything below (DataSourceProductName/Version,
//   ParameterMarkerFormat, identifier quoting, @@IDENTITY, and even the exact constraint-
//   violation message text), so this dialect needs no provider-version-specific branching.
// - Detection: GetSchema("DataSourceInformation").DataSourceProductName returns "MS Jet" — see
//   DatabaseDetectionService.SchemaProductTokens. factory.GetType().FullName is
//   "System.Data.OleDb.OleDbFactory" for ANY OLE DB provider (SQLOLEDB, OraOLEDB, etc.), so no
//   FactoryTypeTokens entry was added — that would misdetect every other OLE DB connection as
//   Access.
// - Parameters: positional "?" only (ParameterMarkerFormat confirmed live); no named-parameter
//   support at all.
// - Identifier quoting: CONFIRMED live that both [brackets] and `backticks` are accepted by the
//   real ACE SQL parser, but ANSI double-quotes are rejected outright ("Syntax error in query.
//   Incomplete query clause."). Brackets chosen as the idiomatic, documented Access convention.
// - No MERGE/ON CONFLICT/ON DUPLICATE KEY of any kind — CONFIRMED live that TableGateway.
//   UpsertAsync correctly throws NotSupportedException as a result (BuildUpsert requires one of
//   SupportsMerge/SupportsInsertOnConflict/SupportsOnDuplicateKey; Access inherits all three as
//   false). Access has no server-side upsert mechanism at all, not just an unimplemented one —
//   this is expected, correct behavior, not a gap to fill in later.
// - No ADO.NET-invocable stored procedures — ProcWrappingStyle.None is a considered decision,
//   not an unexamined default (CLAUDE.md's "Adding a New Database" checklist item 13).
// - Embedded, file-based engine — architecturally in SQLite's category, not a client-server
//   database. IsClientServerDatabase => false, coerced to DbMode.SingleWriter via the shared
//   CoerceEmbeddedSingleWriterMode helper (same call SqliteDialect/DuckDbDialect make). Access
//   has no in-memory mode at all (unlike SQLite's ":memory:"), so DetectInMemoryKind is always
//   InMemoryKind.None.
// - Concurrent-write locking: CONFIRMED live, both the hazard and the fix. Raw OleDb (bypassing
//   pengdows.crud entirely): while one connection holds an open, uncommitted write transaction,
//   a second connection's INSERT of a completely different row into the same table blocks, then
//   fails outright with "OleDbException: Could not update; currently locked." — a genuine, real
//   lock conflict surfaced to the caller, not a transient retry. Through pengdows.crud itself:
//   CoerceConnectionMode coerces DbMode.Best to SingleWriter, and with that governor active, 20
//   concurrent write tasks issued through one shared DatabaseContext all succeed with zero
//   lock-conflict exceptions and all 20 rows land correctly. This empirically justifies
//   SingleWriter as the safe default — it isn't just architecturally reasoned (Jet/ACE's
//   page-level/.laccdb-file locking model resembling SQLite's), the failure it prevents is real
//   and reproducible, and the governor genuinely prevents it for Access specifically.
//   An explicit DbMode.Standard request is honored rather than coerced (allowStandard: true on
//   CoerceEmbeddedSingleWriterMode) — Access is documented by Microsoft as supporting multiple
//   concurrent connections, and a caller who has read that documentation can choose to bypass the
//   governor deliberately; DescribeStandardModeRisk surfaces the CONFIRMED-live failure above as
//   a warning (not a block) when they do.
//   CAVEAT: the script that produced the "CONFIRMED live" finding above was never committed and no
//   longer exists (confirmed via full-repo/git-history search) — unlike DuckDbDialect.cs's
//   equivalent finding, which was independently reproduced and locked into a permanent regression
//   test this session (SerializationConflictTests.cs), this one rests on unreproduced prose. In
//   particular, whether the conflict is scoped to "same table" (as documented here) or narrower
//   ("same row", like DuckDB's confirmed behavior) was never actually tested — see
//   docs/connection/access-concurrency-verification.md for the concrete test plan (Windows-only;
//   this repo's environment can't run OleDb/ACE) to re-verify and, if the granularity turns out to
//   be narrower than "same table", correct this wording.
// - Isolation: CONFIRMED live that only ReadUncommitted and ReadCommitted are accepted by
//   OleDbConnection.BeginTransaction — RepeatableRead/Serializable/Snapshot all throw "Neither
//   the isolation level nor a strengthening of it is supported."
// - Generated keys: CONFIRMED live that SELECT @@IDENTITY works over OLE DB against a real
//   .accdb COUNTER (autoincrement) column, same T-SQL-family idiom SQL Server/Sybase use.
// - Parameter binding: CONFIRMED live (via a real AccessTestProvider CRUD round-trip, not the
//   fakeDb-only unit tests — fakeDb never constructs a real OleDbParameter) that
//   OleDbParameter's own automatic DbType-to-OleDbType mapping for DbType.DateTime does not
//   produce a type Access accepts — every INSERT into a DATETIME column failed with "Data type
//   mismatch in criteria expression" until OleDbType.Date was set explicitly. Fixed via
//   AdvancedTypeRegistry.RegisterTemporalMappings' RegisterMapping<DateTime>(SupportedDatabase.Access, ...)
//   entry (SetEnumProperty reflection-sets OleDbType.Date), the same mechanism Spanner's
//   NpgsqlDbType.TimestampTz fix uses — not a CreateDbParameter override here, to avoid adding a
//   hard System.Data.OleDb reference to this library (matching SqliteDialect's own
//   reflection-based provider-namespace check for the same reason). DateTimeOffset binding for
//   Access has not been verified live and is NOT registered — do not assume the DateTime fix
//   generalizes to it without testing first.
// - Natural-key lookup: Access uses SELECT TOP n (confirmed live), not the base class's generic
//   LIMIT-based fallback — mirrors SybaseDialect's GetNaturalKeySelectClause override.
// - Exception classification: OleDbException DOES derive from DbException (unlike Sybase's
//   AseException) — so, unlike Sybase, this dialect participates in the standard unified
//   constraint-classification delegation (AccessExceptionTranslator delegates to the four
//   IsXxxViolation overrides below rather than re-deriving the logic). CONFIRMED live that
//   OleDbException.ErrorCode is the identical generic COM HRESULT (-2147467259) and
//   OleDbException.Errors is empty for every constraint-violation kind (UNIQUE, NOT NULL, CHECK,
//   FK, PK-duplicate) — no numeric discrimination is possible at all, only English message-text
//   substring matching (mirrors FirebirdDialect's approach). CONFIRMED live that the
//   INSERT-blocked-by-missing-parent and DELETE-blocked-by-child-row FK messages use completely
//   different wording ("a related record is required" vs. "includes related records") — the
//   exact SQL Server pitfall CLAUDE.md's checklist item 22 documents; "related record" is a
//   substring of both, deliberately chosen to cover both shapes.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Microsoft Access (Jet/ACE) dialect, accessed via <c>System.Data.OleDb</c> and the Microsoft
/// Access Database Engine Redistributable — there is no native ADO.NET Jet/ACE client.
/// </summary>
/// <remarks>
/// See the file-level AI SUMMARY for what has been confirmed live (against real
/// <c>Microsoft.ACE.OLEDB.12.0</c> and <c>Microsoft.ACE.OLEDB.16.0</c> providers) vs. reasoned
/// architecturally but not directly stress-tested. See <c>AccessDialectTests.cs</c> for the
/// dedicated capability-flag and exception-classification test coverage (CLAUDE.md's "Adding a
/// New Database" checklist item 7).
/// </remarks>
internal sealed class AccessDialect : SqlDialect
{
    internal AccessDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Access;

    // Positional-only; confirmed live (ParameterMarkerFormat = "?" from GetSchema).
    public override string ParameterMarker => "?";
    public override bool SupportsNamedParameters => false;

    // CONFIRMED live: ANSI double-quotes (SqlDialect's base default) are rejected outright
    // ("Syntax error in query. Incomplete query clause.") — unlike SQL Server, Access has no
    // QUOTED_IDENTIFIER-style session setting to normalize this. [brackets] and `backticks` both
    // work; brackets chosen as the idiomatic, documented Access convention.
    public override string QuotePrefix => "[";
    public override string QuoteSuffix => "]";

    // Embedded, file-based engine — not client-server. Coerced to SingleWriter the same way
    // SqliteDialect/DuckDbDialect are; see file-level AI SUMMARY for the UNVERIFIED caveat on the
    // real concurrent-write behavior this rests on.
    public override bool IsClientServerDatabase => false;
    public override bool IsEmbeddedSingleWriterEngine => true;

    public override InMemoryKind DetectInMemoryKind(string? connectionString) => InMemoryKind.None;

    // Unlike SqliteDialect, an explicit DbMode.Standard request is honored rather than coerced
    // (allowStandard: true) — Access is documented as supporting multiple concurrent connections,
    // so a caller who has read that documentation can opt in deliberately. DbMode.Best still
    // resolves to SingleWriter. See DescribeStandardModeRisk for the CONFIRMED-live risk warning
    // surfaced when this happens.
    public override (DbMode Mode, string Reason) CoerceConnectionMode(DbMode requested, string? connectionString,
        bool isLocalDb) =>
        CoerceEmbeddedSingleWriterMode(requested, InMemoryKind.None, allowStandard: true);

    // CONFIRMED live (see file-level AI SUMMARY): raw OleDb concurrent writers against Access hit
    // "OleDbException: Could not update; currently locked." — stronger evidence than DuckDB's
    // (architecturally-reasoned but not reproduced) caution, so this names the actual failure.
    internal override string DescribeStandardModeRisk() =>
        "Access documents support for multiple concurrent connections, but this was CONFIRMED LIVE " +
        "to fail under concurrent writers: two connections writing to the same table outside " +
        "pengdows.crud's governance produced \"OleDbException: Could not update; currently " +
        "locked.\" pengdows.crud's SingleWriter mode prevents this (verified live: 20 concurrent " +
        "write tasks through one shared DatabaseContext, zero lock-conflict exceptions). Standard " +
        "mode is honored here because it was explicitly requested, but expect intermittent " +
        "lock-conflict failures under real write concurrency unless you serialize writes yourself.";

    // No MERGE/ON CONFLICT/ON DUPLICATE KEY of any kind.
    public override bool SupportsMerge => false;

    // No ADO.NET-invocable stored procedures. Deliberate decision, not an unexamined default.
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

    // Access has no native UUID/GUID type — store as a client-generated string, matching every
    // other dialect without native UUID column support. CONFIRMED live: a Guid parameter bound
    // via ApplyGuidFormat's DbType.String reassignment (not DbType.Guid) round-trips correctly;
    // also confirmed live that a decimal parameter binds correctly against a CURRENCY column as
    // either DbType.Decimal or DbType.Currency — neither has the DbType.DateTime-style
    // OleDbType-mapping problem found elsewhere in this file.
    protected override GuidStorageFormat GuidFormat => GuidStorageFormat.String;

    // CONFIRMED live: RepeatableRead/Serializable/Snapshot all throw "Neither the isolation
    // level nor a strengthening of it is supported."
    internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) => new()
    {
        IsolationLevel.ReadUncommitted,
        IsolationLevel.ReadCommitted
    };

    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) => new()
    {
        [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
        // Serializable is unavailable — ReadCommitted is the strictest level genuinely accepted.
        [IsolationProfile.StrictConsistency] = IsolationLevel.ReadCommitted,
        [IsolationProfile.FastWithRisks] = IsolationLevel.ReadUncommitted
    };

    // CONFIRMED live: SELECT @@IDENTITY works over OLE DB against a real .accdb COUNTER column,
    // and is still used by TableGateway.Core.cs's PopulateGeneratedIdAsync fallback path.
    public override string GetLastInsertedIdQuery() => "SELECT @@IDENTITY";

    // Deliberately NOT overridden to true, unlike Sybase's identical-looking @@IDENTITY case.
    // CONFIRMED live that Access/OleDb rejects multi-statement batches outright ("Characters
    // found after end of SQL statement") when trying "INSERT ...; SELECT @@IDENTITY" as one
    // command — so, unlike Sybase, GeneratedKeyPlan.CompoundStatement is not available here to
    // safely pair with a session-scoped id function. Leaving this at the base false means
    // GetGeneratedKeyPlan()'s default correctly falls through to CorrelationToken instead of the
    // categorically-unsafe SessionScopedFunction (GeneratedKeyPlanReachabilityTests forbids any
    // dialect defaulting to it — the same pooled-connection hazard SAP HANA hit; see HanaDialect's
    // remarks for the precedent of leaving this at the base default rather than overriding
    // GetGeneratedKeyPlan() directly).

    // Confirmed live: SELECT TOP n, not the base class's generic LIMIT-based fallback — mirrors
    // SybaseDialect's override.
    protected override string GetNaturalKeySelectClause(string wrappedIdColumn) => $"SELECT TOP 1 {wrappedIdColumn}";
    protected override string GetNaturalKeyFirstRowOnlyClause() => string.Empty;

    public override string GetVersionQuery() => string.Empty;

    /// <summary>
    /// There is no SQL-queryable version function in Jet SQL at all (confirmed by this dialect
    /// having no <c>SET</c> statement support either — see the session-settings remarks above).
    /// CONFIRMED live: <c>OleDbConnection.ServerVersion</c> returns the same "04.00.0000"
    /// Jet-compatibility version string reported by <c>DataSourceProductVersion</c>, populated
    /// directly by the driver — same idiom as FlatFileDialect/InterBaseDialect's own
    /// ServerVersion-based override, for the identical reason (no version()-style SQL function).
    /// </summary>
    public override Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        return Task.FromResult(connection.ServerVersion);
    }

    // CONFIRMED live: Jet SQL has no session-level SET statement mechanism at all — the parser
    // only recognizes DELETE/INSERT/PROCEDURE/SELECT/UPDATE as valid top-level statements ("SET
    // ANSI_NULLS ON"/"SET QUOTED_IDENTIFIER ON"/"SET TRANSACTION ISOLATION LEVEL READ COMMITTED"
    // all fail with "Invalid SQL statement; expected 'DELETE', 'INSERT', 'PROCEDURE', 'SELECT',
    // or 'UPDATE'."). Any engine-level behavior (locking mode, engine type) is controlled purely
    // via OLE DB connection-string properties at connect time, not runtime SQL — there is no
    // pooled-connection session-state hazard for GetBaseSessionSettings (CLAUDE.md's "Adding a
    // New Database" checklist item 12) to guard against here. Deliberately not overridden —
    // stays at the base class's empty-string default.

    public override string ExtractProductNameFromVersion(string versionString) => "MS Jet";

    // ---- Constraint-kind classification: pure message-substring matching (see file-level AI
    // SUMMARY — OleDbException carries no discriminable numeric error code for any violation
    // kind). Exact text captured live against a real .accdb, identical on ACE 12.0 and 16.0.

    public override bool IsUniqueViolation(DbException ex) =>
        ex.Message.Contains("duplicate values in the index, primary key, or relationship",
            StringComparison.OrdinalIgnoreCase);

    public override bool IsNotNullViolation(DbException ex) =>
        ex.Message.Contains("must enter a value", StringComparison.OrdinalIgnoreCase);

    public override bool IsCheckConstraintViolation(DbException ex) =>
        ex.Message.Contains("prohibited by the validation rule", StringComparison.OrdinalIgnoreCase);

    // Covers both real message shapes confirmed live: INSERT blocked by a missing parent row
    // ("a related record is required") and DELETE blocked by an existing child row ("includes
    // related records") — "related record" is a substring of both.
    public override bool IsForeignKeyViolation(DbException ex) =>
        ex.Message.Contains("related record", StringComparison.OrdinalIgnoreCase);

    // CONFIRMED live this session: a second connection writing to a row/page held by another
    // connection's open transaction blocks, then fails outright with this exact message once
    // contention resolves — a genuine lock-WAIT scenario (blocks, then gives up), not a detected
    // circular-wait deadlock. Matches HanaDialect's own "lock wait timeout" precedent (SAP error
    // 131 -> Timeout), not its "detected deadlock" one (error 133 -> Deadlock). Feeds both the
    // advisory AnalyzeException API and, via AccessExceptionTranslator's
    // DbExceptionTranslationSupport.TryCreateFromCategory call, the actual thrown exception type
    // (CommandTimeoutException, IsTransient = true) — so a caller retrying on IsTransient does
    // the right thing instead of this falling through to a generic DatabaseOperationException.
    protected override bool TryClassifyProviderException(DbException ex, out DbErrorCategory category)
    {
        if (ex.Message.Contains("currently locked", StringComparison.OrdinalIgnoreCase))
        {
            category = DbErrorCategory.Timeout;
            return true;
        }

        category = DbErrorCategory.Unknown;
        return false;
    }
}
