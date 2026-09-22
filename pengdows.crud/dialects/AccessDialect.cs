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
//   RE-VERIFIED LIVE end-to-end on a real Windows machine (see
//   docs/connection/access-concurrency-verification.md for the full run): the earlier finding
//   above was correct in substance but the connection-string bug just above (SupportsExternalPooling)
//   meant DbMode.Standard couldn't even open a connection until that was fixed. Once fixed:
//   (1) 20 concurrent single-statement auto-committing INSERTs into disjoint rows of the same
//   table via DbMode.Standard all succeeded, 20/20, three runs in a row — a bare INSERT holds its
//   implicit lock too briefly to collide even under real concurrency, so this alone is NOT
//   sufficient evidence the hazard doesn't exist.
//   (2) Rerun with the SAME contention shape the original raw-OleDb finding used — an explicitly
//   held transaction (INSERT, then an artificial 200ms delay, then Commit) via
//   Context.BeginTransaction() through the actual public API — reproduced the hazard clearly:
//   18/20, 18/20, and 14/20 failures across three runs, all
//   CommandTimeoutException("... Could not update; currently locked."), while the identical
//   workload under DbMode.SingleWriter stayed at 0/20 failures across three runs. Raw OleDb
//   (bypassing pengdows.crud) with the same disjoint-rows-same-table shape separately reproduced
//   3-8/20 failures across three runs. Raw OleDb with disjoint rows across TWO DIFFERENT tables in
//   the same file also failed (3-6/20 across three runs, including failures in a table no writer
//   assigned to it ever touched) — confirming the conflict is NOT table-scoped, matching (not
//   narrower than) this dialect's original "same table" framing; unlike DuckDB, no narrower
//   same-row framing applies here.
//   BOTTOM LINE (2026-09-18 re-verification): SingleWriter is the only DbMode confirmed both
//   correct AND fully concurrent for Access. Standard is a real, working opt-in (after the
//   connection-string fixes below) but is confirmed UNSAFE under any workload holding a
//   transaction open across more than one round trip. SingleConnection would also be
//   correctness-safe but serializes ALL access (including reads), so it is not a practical
//   alternative — just a strictly worse option for this engine's use case.
// - Connection-string bugs found and fixed this session (2026-09-18), all confirmed live on a
//   real Windows machine against a real .accdb — none of these were caught by the fakeDb-backed
//   unit-test suite, since fakeDb never constructs a real OleDbConnection:
//   (1) SupportsExternalPooling/PoolingSettingName: the SqlDialect base defaults (true/"Pooling")
//   caused ConnectionPoolingConfiguration.ApplyPoolingDefaults to inject an ADO.NET-style
//   "Pooling=True" into the connection string under DbMode.Standard/PreventDatabaseUnload
//   (SingleWriter's own StripPoolingSetting call was masking this). Jet/ACE has no such keyword
//   and throws "OleDbException: Could not find installable ISAM" for ANY unrecognized connection
//   property — confirmed both "Pooling=True" and "Pooling=False" fail identically, and separately
//   that "Application Name" fails the exact same way (so ApplicationNameSettingName correctly
//   stays at the base null). This was not a documented risk, it was a hard failure on EVERY
//   DbMode.Standard connection, including a single non-concurrent CREATE TABLE with zero
//   contention (5/5, then 7/7 reproductions across two isolated re-runs). Fixed by overriding both
//   to false/null, architecturally matching DuckDbDialect's "in-process, no external pooling
//   switch" rationale. CORRECTION (re-examined after a fair challenge to the original claim
//   here): the original justification — "System.Data.OleDb pools connections transparently by
//   default anyway, so nothing is lost" — was NOT actually supported by the evidence and has
//   been retracted. Fast repeat opens (50 sequential opens averaging 1.56ms after warmup) do NOT
//   by themselves prove pooling: a dedicated discriminating test (30 opens reusing one connection
//   string vs. 30 opens each against a freshly-created, never-before-seen connection string) came
//   back statistically indistinguishable (1.44ms vs. 1.64ms avg — a 1.14x ratio, not the multi-x
//   difference real string-keyed pooling would produce), and explicitly disabling native OLE DB
//   pooling/session services (OLE DB Services=-4) made opens marginally FASTER (0.93x), not
//   slower. The honest conclusion: pooling isn't providing any measurable benefit here, not
//   because it's transparently already happening, but because a local file attach with no
//   network handshake or auth negotiation has nothing expensive for pooling to amortize in the
//   first place — architecturally closer to why DuckDB/FlatFile treat external pooling as not a
//   meaningful concept at all, not because ACE secretly pools under the hood. The fix
//   (SupportsExternalPooling => false) is unchanged and still correct; only this reasoning was
//   wrong. The real, native OLE DB lever for connection-time services is "OLE DB Services=-N"
//   (confirmed recognized), a different mechanism entirely from ADO.NET's common "Pooling"
//   keyword — but confirmed to make no measurable difference either way for this provider.
//   (2) ReadOnlyPoolDiscriminatorSettingName/Value: without ApplicationNameSettingName, this
//   dialect had no way to differentiate its reader and writer connection strings, so
//   System.Data.OleDb's own pool (keyed by exact connection-string text) collapsed them into one
//   shared physical pool — mirrors the exact problem OracleDialect already solved via
//   ReadOnlyPoolDiscriminatorSettingName => "Metadata Pooling"/"false". Fixed using
//   "Jet OLEDB:Database Locking Mode" => "1" — confirmed live to be both recognized by ACE and
//   behaviorally inert (4 runs each of the same 20-writer held-transaction contention pattern
//   showed statistically indistinguishable failure rates with vs. without it explicitly set,
//   ~13-14/20 either way), because 1 (row-level locking) is already ACE's own documented default
//   for Access 2000+/.accdb.
// - Read-only connection mode: GetReadOnlyConnectionParameter() => "Mode=Read" — CONFIRMED live
//   this is a real, recognized OLE DB/Jet property (distinct from the unrecognized ADO.NET
//   keywords above) that genuinely enforces read-only at the driver level: a write against a
//   Mode=Read connection fails with "Operation must use an updateable query." (classified as
//   DbErrorCategory.ReadOnlyViolation below, producing a real ReadOnlyViolationException end to
//   end — confirmed via a live TableGateway CRUD round trip, not just fakeDb unit tests), while a
//   Mode=ReadWrite control connection succeeds normally. Also confirmed live (3/3 trials) that a
//   held-open Mode=Read connection actively issuing reads does NOT block a concurrent writer on
//   the same file — unlike DuckDbDialect.ReadOnlyConnectionsCanBlockConcurrentWriters (true), so
//   Access correctly keeps the SqlDialect base default (false) with no override.
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
    // SqliteDialect/DuckDbDialect are; see file-level AI SUMMARY for the CONFIRMED-live
    // concurrent-write numbers this rests on (re-verified 2026-09-18 — no longer unverified).
    public override bool IsClientServerDatabase => false;
    public override bool IsEmbeddedSingleWriterEngine => true;

    // CONFIRMED LIVE (re-verified on a real Windows machine, this session): the SqlDialect base
    // defaults (SupportsExternalPooling => true, PoolingSettingName => "Pooling") are wrong for
    // Access and were an outright bug, not merely a documented risk. ApplyPoolingDefaults only
    // runs for Standard/PreventDatabaseUnload/SingleWriter modes; SingleWriter's own
    // StripPoolingSetting call was masking the bug by removing the injected keyword again, so
    // every DbMode.Standard connection — including a single, non-concurrent CREATE TABLE with
    // zero contention — reproducibly (5/5, then 7/7 across two isolated re-runs) failed with
    // "OleDbException: Could not find installable ISAM" the instant it tried to open. Root cause,
    // confirmed by direct reproduction with a raw connection string: ApplyPoolingDefaults injects
    // an ADO.NET-style "Pooling=True" keyword that Jet/ACE's OLE DB provider does not recognize —
    // Jet/ACE surfaces ANY unrecognized connection property through this exact generic
    // ISAM-driver-selection error, unrelated to its literal meaning ("Pooling=False" reproduces
    // the identical error). Access has no ADO.NET-style external pooling switch at all —
    // architecturally identical to DuckDbDialect's SupportsExternalPooling => false ("in-process"),
    // not a client-server provider concept — so both must be false/null here.
    public override bool SupportsExternalPooling => false;
    public override string? PoolingSettingName => null;

    // ApplicationNameSettingName stays at the base default (null) — CONFIRMED live that
    // "Application Name" is equally unrecognized by ACE as "Pooling" was, so no dialect could
    // ever set it here without reproducing the same "Could not find installable ISAM" failure.
    // Without an ApplicationNameSettingName, BuildReaderConnectionString's own fallback
    // (DatabaseContext.Initialization.cs) never fires for this dialect, so reader and writer
    // connection strings end up identical and System.Data.OleDb's own pool (keyed by exact
    // connection-string text) collapses them into one shared physical pool. Mirrors
    // OracleDialect.ReadOnlyPoolDiscriminatorSettingName's identical fix for ODP.NET — CONFIRMED
    // live that "Jet OLEDB:Database Locking Mode=1" is both recognized by ACE and behaviorally
    // inert (4 runs each of the same 20-writer contention pattern showed ~13-14/20 failures with
    // or without it explicitly set), because 1 (row-level locking) is already ACE's own
    // documented default for Access 2000+/.accdb — so setting it explicitly on the reader
    // connection string only, differentiates the pool key without changing real behavior.
    internal override string? ReadOnlyPoolDiscriminatorSettingName => "Jet OLEDB:Database Locking Mode";
    internal override string? ReadOnlyPoolDiscriminatorSettingValue => "1";

    public override InMemoryKind DetectInMemoryKind(string? connectionString) => InMemoryKind.None;

    // Unlike SqliteDialect, an explicit DbMode.Standard request is honored rather than coerced
    // (allowStandard: true) — Access is documented as supporting multiple concurrent connections,
    // so a caller who has read that documentation can opt in deliberately. DbMode.Best still
    // resolves to SingleWriter. See DescribeStandardModeRisk for the CONFIRMED-live risk warning
    // surfaced when this happens.
    public override (DbMode Mode, string Reason) CoerceConnectionMode(DbMode requested, string? connectionString,
        bool isLocalDb) =>
        CoerceEmbeddedSingleWriterMode(requested, InMemoryKind.None, allowStandard: true);

    // CONFIRMED LIVE, re-verified end-to-end through pengdows.crud's own public API (not just raw
    // OleDb) — see file-level AI SUMMARY for the full numbers and the fair-comparison methodology
    // (bare auto-committing INSERTs do NOT reproduce this; an explicitly held transaction does).
    internal override string DescribeStandardModeRisk() =>
        "Access documents support for multiple concurrent connections, but this was CONFIRMED LIVE " +
        "to fail under concurrent writers, reproduced through pengdows.crud's own transaction API " +
        "(not just raw OleDb): 20 concurrent writers each holding an open transaction produced " +
        "14-18/20 CommandTimeoutException failures (\"Could not update; currently locked.\") under " +
        "DbMode.Standard, vs. 0/20 under DbMode.SingleWriter for the identical workload. Unlike " +
        "DuckDB (whose Standard-mode risk has a genuine, confirmed safe zone — disjoint-row " +
        "writers proceed cleanly, only same-row contention fails), Access has no safe zone here: " +
        "concurrent writers touching completely disjoint rows in the same table, and even disjoint " +
        "rows across two different tables in the same file, still failed. \"Just avoid writing " +
        "the same row concurrently\" does not make Standard mode safe for Access the way it does " +
        "for DuckDB. Standard mode is honored here because it was explicitly requested, but expect " +
        "lock-conflict failures under real write concurrency (an open transaction spanning more " +
        "than one round trip) unless you serialize writes yourself.";

    // No MERGE/ON CONFLICT/ON DUPLICATE KEY of any kind.
    public override bool SupportsMerge => false;

    // Jet/ACE SQL has no TRUNCATE TABLE statement; a caller must DELETE FROM the table instead.
    public override bool SupportsTruncateTable => false;

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

    // CONFIRMED live: "Mode=Read" is a real, recognized OLE DB/Jet connection-string property
    // (distinct from ADO.NET's own "Pooling"/"Application Name" keywords, both confirmed
    // unrecognized above) that genuinely enforces read-only at the driver level — a write
    // attempted against a Mode=Read connection fails with "Operation must use an updateable
    // query.", while a Mode=ReadWrite control connection succeeds normally. Mirrors
    // SqliteDialect's "Mode=ReadOnly" (a different provider's spelling of the same idea).
    public override string? GetReadOnlyConnectionParameter() => "Mode=Read";

    // CONFIRMED live (3/3 trials): a held-open Mode=Read connection, actively issuing reads,
    // does NOT block a concurrent writer on the same file — unlike DuckDbDialect's confirmed
    // true here. Access stays at the SqlDialect base default (false); no override needed, but
    // documented explicitly since the value matters and was verified, not assumed.

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

        // CONFIRMED live: the exact message a real ACE connection opened with "Mode=Read"
        // (GetReadOnlyConnectionParameter above) returns when a write is attempted against it.
        // Feeds both the advisory AnalyzeException API and, via AccessExceptionTranslator's
        // TryCreateFromCategory call, the actual thrown ReadOnlyViolationException — mirroring
        // SqliteDialect/DuckDbDialect's identical ReadOnlyViolation classification.
        if (ex.Message.Contains("must use an updateable query", StringComparison.OrdinalIgnoreCase))
        {
            category = DbErrorCategory.ReadOnlyViolation;
            return true;
        }

        category = DbErrorCategory.Unknown;
        return false;
    }
}
