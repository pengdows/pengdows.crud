// =============================================================================
// FILE: InterBaseDialect.cs
// PURPOSE: Embarcadero InterBase dialect implementation.
//
// AI SUMMARY:
// - Backported from pengdows.crud 3.0 (same live-verification trail applies) to this
//   non-breaking 2.0.6 patch line. Isolation-level data lives in IsolationResolver.cs's central
//   switch instead, and the " ROWS 1" natural-key first-row clause lives inline in
//   SqlDialect.cs's GetNaturalKeyLookupQuery (2.0.6 predates 3.0's GetNaturalKeyFirstRowOnlyClause
//   hook refactor).
// - LIVE-VERIFIED end-to-end against a real InterBase 15.1.0.42 (development) server running in
//   Docker (a node-locked Developer Edition license registered to the container's static IP),
//   using the real InterBaseSql.Data.InterBaseClient 10.0.3 ADO.NET driver: named parameter
//   binding, ROWS-based paging, MERGE/IDENTITY/CREATE SEQUENCE/RETURNING/multi-row-VALUES
//   rejection, classic GENERATOR + GEN_ID sequence emulation, stored procedure call syntax, a
//   full-cycle savepoint (create/rollback-to/release, verified by an actual before/after
//   row-survival check), all five ANSI/MVCC isolation levels, a GUID round-trip through
//   CHAR(16) CHARACTER SET OCTETS, and live constraint-violation exception codes/messages for
//   all four constraint kinds.
// - InterBase and Firebird share a common ancestor (InterBase 6.0, forked in 2000) but have
//   diverged in real, confirmed ways since — this dialect was built from InterBase's own live
//   behavior throughout, not adapted from FirebirdDialect.cs by assumption.
// - Driver: InterBaseSql.Data.InterBaseClient (Embarcadero, open source, NuGet.org, .NET 8
//   target). Factory: InterBaseSql.Data.InterBaseClient.InterBaseClientFactory.Instance.
//   GetSchema("DataSourceInformation") reports DataSourceProductName = "InterBase",
//   ParameterNameMaxLength = 128, IdentifierCase = 1 (Insensitive/uppercase-folds unquoted
//   identifiers), QuotedIdentifierCase = 2 (Sensitive) — confirmed live.
// - Parameters: named "@name" CONFIRMED live via a real parameterized INSERT and a parameterized
//   SELECT WHERE clause (bare positional "?" also works when the driver can infer type from
//   context). A bare "SELECT @x FROM rdb$database" with nothing else to infer a type from fails
//   with "Data type unknown" (SQLCODE -804) — a context-dependent DSQL type-inference limitation
//   shared with Firebird, not a sign that named parameters don't work.
// - Identifier quoting: ANSI double quotes (base class default). CONFIRMED live via
//   IdentifierCase=1 / QuotedIdentifierCase=2, matching Oracle/HANA's fold-to-uppercase-when-
//   unquoted, case-sensitive-when-quoted behavior.
// - Pagination: no SQL:2008 OFFSET/FETCH, no MySQL/PostgreSQL-style LIMIT/OFFSET. InterBase's own
//   idiom is "ROWS n" (limit only) and "ROWS m TO n" (1-based, INCLUSIVE range) — CONFIRMED live
//   for both forms; overridden in AppendPaging below. Also used for the natural-key lookup
//   fallback's first-row-only clause (" ROWS 1"), same pattern FirebirdDialect uses.
// - MERGE: CONFIRMED REJECTED outright at the MERGE keyword itself (SQLCODE -104, "Token unknown
//   ... MERGE"). SupportsMerge left at the base class's default false — no override needed.
// - Batch insert: CONFIRMED REJECTED — the ANSI multi-row VALUES clause fails with SQLCODE -104
//   at the comma. Falls back to one INSERT per entity, same as SQLite/MySQL/MariaDB/Firebird/
//   Informix/HANA.
// - Generated keys: no IDENTITY columns (CONFIRMED REJECTED, SQLCODE -104 at GENERATED), no
//   CREATE SEQUENCE / NEXT VALUE FOR (CONFIRMED REJECTED, SQLCODE -104 at SEQUENCE), no INSERT ...
//   RETURNING (CONFIRMED REJECTED, SQLCODE -104 at RETURNING) — this is the classic pre-Firebird
//   InterBase 6 generator model throughout. The working mechanism is the original
//   "CREATE GENERATOR name" + "GEN_ID(name, 1)" pair, CONFIRMED live. GeneratedKeyPlan.
//   PrefetchSequence already has real production usage on this branch (OracleDialect uses it
//   too), so this is a proven, not experimental, mechanism here.
// - Procedures: SELECT * FROM proc(args) (read) / EXECUTE PROCEDURE proc(args) (write), both
//   CONFIRMED live against a real SUSPEND-based selectable procedure — matches
//   ProcWrappingStyle.ExecuteProcedure (Firebird's own style).
// - Savepoints: SAVEPOINT / ROLLBACK TO SAVEPOINT / RELEASE SAVEPOINT all CONFIRMED live, via both
//   raw SQL and the typed DbTransaction.Save/Rollback/Release API, including a semantic
//   before/after row-survival check — full capability set, same as HANA.
// - Isolation levels: InterBase's IBTransaction.BeginTransaction CONFIRMED to accept all five of
//   ReadUncommitted, ReadCommitted, RepeatableRead, Serializable, and Snapshot without error —
//   broader than the four HANA supports (HANA rejects Snapshot). Whether ReadUncommitted delivers
//   genuine dirty reads server-side was not independently verified.
// - Exception classification: IBException.ErrorCode reliably surfaces InterBase's real ISC status
//   code (confirmed NOT a generic HRESULT, unlike HANA's driver) — reflection finds it via
//   DbException.ErrorCode directly, since IBException has no "Number"/"SqliteErrorCode"/
//   "NativeError" property to shadow it. Real codes captured live: 335544665 (unique/PK, both an
//   unnamed PK and a named UNIQUE constraint), 335544466 (foreign key, confirmed both
//   directions), 335544347 (NOT NULL), 335544558 (CHECK). Deadlock/lock-timeout codes were not
//   reproduced (would require two contending sessions) — left unclassified at the category
//   level.
// - A genuinely surprising, non-ANSI-standard finding: a CHECK constraint on a nullable column
//   REJECTS a NULL value outright (CONFIRMED live). Standard SQL treats "chk > 0" against NULL as
//   UNKNOWN, which a CHECK constraint normally treats as passing — InterBase does not follow that
//   rule. This is a schema-design fact for callers, not something this dialect's capability flags
//   need to model.
// - GUIDs: IBDbType.Guid exists and, CONFIRMED live, round-trips a native System.Guid correctly
//   through a CHAR(16) CHARACTER SET OCTETS column when using the driver's own typed parameter.
//   For pengdows.crud's own generic write path (a plain DbType.Binary parameter carrying a
//   byte[]): CONFIRMED live that InterBaseSql.Data.InterBaseClient interprets the 16 bytes as
//   RFC 4122 big-endian, not .NET's mixed-endian ToByteArray() layout — the same big-endian byte
//   swap FirebirdDialect.SerializeGuidAsBinary already uses was CONFIRMED live to round-trip
//   correctly here too.
// - Pool discriminator: CONFIRMED via reflection against the real IBConnectionStringBuilder
//   (10.0.3, 33 properties) that no ApplicationName-equivalent keyword exists. "fetch size=200"
//   was chosen — a real, recognized keyword that matches the driver's own compiled-in default,
//   so setting it explicitly is guaranteed behaviorally inert.
// - Read-only transactions: CONFIRMED LIVE that the driver genuinely supports read-only
//   transactions via IBTransactionOptions at transaction-creation time, but this is NOT wired
//   into pengdows.crud's TryEnterReadOnlyTransaction hook (which runs SQL after the transaction
//   is already open) — InterBase/Firebird's TPB-based transaction model requires the read-only
//   flag at the moment the physical transaction handle is created, not as a follow-up statement.
//   Implementing this properly needs a new pengdows.crud extension point letting a dialect
//   control transaction creation itself — left unimplemented rather than forcing a broken
//   override.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.exceptions.translators;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Embarcadero InterBase dialect.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Generated keys:</strong> Uses the classic InterBase 6-style
/// <c>CREATE GENERATOR</c> / <c>GEN_ID(name, 1)</c> pair via
/// <see cref="GeneratedKeyPlan.PrefetchSequence"/> — InterBase supports neither IDENTITY columns
/// nor <c>CREATE SEQUENCE</c> nor <c>INSERT ... RETURNING</c>.
/// </para>
/// <para>
/// <strong>Parameters:</strong> Named "@name".
/// </para>
/// </remarks>
internal sealed class InterBaseDialect : SqlDialect
{
    internal InterBaseDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.InterBase;

    // CONFIRMED via reflection against the real InterBaseSql.Data.InterBaseClient.
    // IBConnectionStringBuilder (10.0.3): no ApplicationName-equivalent keyword exists among its
    // 33 properties. "fetch size" was chosen and CONFIRMED LIVE: it is a real, recognized keyword,
    // and 200 is FetchSize's own compiled-in default.
    internal override string? ReadOnlyPoolDiscriminatorSettingName => "fetch size";
    internal override string? ReadOnlyPoolDiscriminatorSettingValue => "200";

    // Named "@name" parameters, CONFIRMED live via a real parameterized INSERT/SELECT — see
    // file-level AI SUMMARY for the bare-SELECT type-inference caveat.
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;

    // Confirmed via DataSourceInformation.ParameterNameMaxLength.
    public override int ParameterNameMaxLength => 128;

    // CONFIRMED live: neither SQL:2008 OFFSET/FETCH nor MySQL/PostgreSQL-style LIMIT/OFFSET is
    // accepted — see AppendPaging below for InterBase's actual ROWS-based syntax.
    public override bool SupportsOffsetFetch => false;
    public override bool SupportsLimitOffset => false;

    // CONFIRMED live: the ANSI multi-row VALUES clause is rejected (SQLCODE -104 at the comma).
    // Falls back to one BuildCreate per entity, same safe path SQLite/MySQL/MariaDB/Firebird/
    // Informix/HANA use for the same reason.
    public override bool SupportsBatchInsert => false;

    // CONFIRMED live: "SELECT * FROM proc(args)" (read) / "EXECUTE PROCEDURE proc(args)" (write)
    // both work against a real SUSPEND-based procedure — same style as Firebird.
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.ExecuteProcedure;

    // CONFIRMED REJECTED live: "GENERATED BY DEFAULT AS IDENTITY" fails with SQLCODE -104 at
    // GENERATED. Generated keys use the classic generator/GEN_ID mechanism instead — see
    // GetGeneratedKeyPlan/GetSequenceNextValQuery below.
    public override bool SupportsIdentityColumns => false;

    // CONFIRMED REJECTED live: "DROP TABLE IF EXISTS" fails with SQLCODE -104 at IF.
    public override bool SupportsDropTableIfExists => false;

    // CONFIRMED live: SAVEPOINT / ROLLBACK TO SAVEPOINT / RELEASE SAVEPOINT all succeed, via both
    // raw SQL and the typed DbTransaction API, with a verified before/after row-survival check.
    public override bool SupportsSavepoints => true;

    // GUIDs stored as 16 raw bytes in a CHAR(16) CHARACTER SET OCTETS column. CONFIRMED live that
    // the driver reads this column type back as a native Guid using RFC 4122 big-endian byte
    // order. SerializeGuidAsBinary below applies the matching swap.
    protected override GuidStorageFormat GuidFormat => GuidStorageFormat.Binary;

    /// <summary>
    /// Serializes a <see cref="Guid"/> to 16 bytes in RFC 4122 big-endian byte order — CONFIRMED
    /// live that InterBaseSql.Data.InterBaseClient reads a CHAR(16) CHARACTER SET OCTETS column
    /// back as a <see cref="Guid"/> using this same big-endian interpretation (identical to
    /// FirebirdDialect's own confirmed behavior, verified independently for this driver rather
    /// than assumed from Firebird). .NET's <see cref="Guid.ToByteArray()"/> uses mixed-endian
    /// (Data1/2/3 little-endian), so the first three components are swapped after calling it.
    /// </summary>
    protected override byte[] SerializeGuidAsBinary(Guid guid)
    {
        var bytes = guid.ToByteArray();
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
        return bytes;
    }

    /// <summary>
    /// CONFIRMED live: InterBaseSql.Data.InterBaseClient rejects a raw <see cref="DbType.DateTimeOffset"/>
    /// parameter outright at the driver level ("Invalid data type: 27") — there is no time-zone-aware
    /// temporal type in InterBase's type catalog at all. Same real limitation
    /// FirebirdSql.Data.FirebirdClient has for the identical reason — coerce to UTC
    /// <see cref="DateTime"/> before it ever reaches the driver, confirmed live to round-trip
    /// correctly once coerced (<see cref="DateTimeKind.Unspecified"/> prevents the provider from
    /// applying its own timezone adjustment on top).
    /// </summary>
    public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        if (type == DbType.DateTimeOffset && value is DateTimeOffset dto)
        {
            return base.CreateDbParameter<object?>(name, DbType.DateTime,
                DateTime.SpecifyKind(dto.UtcDateTime, DateTimeKind.Unspecified));
        }

        return base.CreateDbParameter(name, type, value);
    }

    /// <summary>
    /// InterBase's own paging idiom: "ROWS n" for a limit with no offset, "ROWS m TO n" for a
    /// 1-based INCLUSIVE range — CONFIRMED live for both forms. Neither SQL:2008 OFFSET/FETCH nor
    /// MySQL/PostgreSQL-style LIMIT/OFFSET is accepted (see SupportsOffsetFetch/SupportsLimitOffset
    /// above).
    /// </summary>
    public override void AppendPaging(ISqlQueryBuilder query, int offset, int limit)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Must be >= 0.");
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Must be > 0.");
        }

        if (offset == 0)
        {
            query.Append(" ROWS ").Append(limit);
        }
        else
        {
            // 1-based, inclusive: row (offset+1) through row (offset+limit).
            query.Append(" ROWS ").Append(offset + 1).Append(" TO ").Append(offset + limit);
        }
    }

    // InterBase's "limit to one row" clause for the natural-key lookup fallback is " ROWS 1", not
    // the base class's default " LIMIT 1" - handled inline in SqlDialect.cs's
    // GetNaturalKeyLookupQuery on this branch (2.0.6 predates 3.0's
    // GetNaturalKeyFirstRowOnlyClause hook refactor), not overridable here.

    /// <summary>
    /// InterBase supports neither IDENTITY columns, CREATE SEQUENCE, nor INSERT ... RETURNING
    /// (all CONFIRMED REJECTED live — see file-level AI SUMMARY) — the base class's
    /// SupportsInsertReturning/HasSessionScopedLastIdFunction-driven default would otherwise fall
    /// all the way through to CorrelationToken. InterBase's real, working mechanism is the classic
    /// InterBase 6 "CREATE GENERATOR name" + "GEN_ID(name, 1)" pair (CONFIRMED live), which
    /// GeneratedKeyPlan.PrefetchSequence already has real production usage for on this branch
    /// (OracleDialect uses the same plan).
    /// </summary>
    public override GeneratedKeyPlan GetGeneratedKeyPlan() => GeneratedKeyPlan.PrefetchSequence;

    /// <summary>
    /// The classic InterBase 6 generator-read syntax — CONFIRMED live to return the expected
    /// sequential value. The generator itself must already exist (created via CREATE GENERATOR
    /// against whatever sequence-naming convention the caller's schema uses).
    /// </summary>
    public override string GetSequenceNextValQuery(string sequenceName)
    {
        return $"SELECT GEN_ID({WrapObjectName(sequenceName)}, 1) FROM RDB$DATABASE";
    }

    // Isolation-level data (GetSupportedIsolationLevels/GetIsolationProfileMapping on 3.0) lives
    // in IsolationResolver.cs's SupportedDatabase.InterBase cases on this branch instead.

    // All four codes below were captured live from a real InterBaseSql.Data.InterBaseClient
    // IBException thrown against a real InterBase 15 server (see file-level AI SUMMARY).
    // IBException.ErrorCode reliably carries the real ISC status code (confirmed via a live
    // property-enumeration of IBException: it has no "Number"/"SqliteErrorCode"/"NativeError"
    // property, so DbExceptionTranslationSupport's reflection probe falls through to the base
    // DbException.ErrorCode, which IS the real code for this driver).
    public override bool IsUniqueViolation(DbException ex) =>
        DbExceptionTranslationSupport.TryGetErrorCode(ex) == 335544665;

    // 335544466: CONFIRMED live for BOTH directions — insert/update referencing a missing parent,
    // and delete/update of a parent still referenced by a child — same code both ways.
    public override bool IsForeignKeyViolation(DbException ex) =>
        DbExceptionTranslationSupport.TryGetErrorCode(ex) == 335544466;

    // 335544347: "validation error for column X, value \"*** null ***\"" — a DISTINCT code from
    // CHECK (335544558), confirmed live. No message-text discrimination needed.
    public override bool IsNotNullViolation(DbException ex) =>
        DbExceptionTranslationSupport.TryGetErrorCode(ex) == 335544347;

    public override bool IsCheckConstraintViolation(DbException ex) =>
        DbExceptionTranslationSupport.TryGetErrorCode(ex) == 335544558;

    /// <summary>
    /// IBConnection.ServerVersion (e.g. "LI-V15.1.0.42/tcp (development)/P15") is populated
    /// directly from the wire protocol handshake — there is no SQL-queryable version function.
    /// CONFIRMED live: neither Firebird's own "rdb$get_context('SYSTEM','ENGINE_VERSION')" nor a
    /// mon$-table equivalent exists in InterBase 15 (both fail with "Function unknown"/table not
    /// found).
    /// </summary>
    public override Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        return Task.FromResult(connection.ServerVersion);
    }

    public override string ExtractProductNameFromVersion(string versionString)
    {
        return "InterBase";
    }

    /// <summary>
    /// InterBase's ServerVersion uses the same legacy "LI-Vmajor.minor.build" format Firebird
    /// inherited from the same codebase (e.g. "LI-V15.1.0.42/tcp (development)/P15"). CONFIRMED
    /// live that the base class's own generic dotted-version regex already parses this exact real
    /// string correctly — this override's own legacy regex is structurally redundant for every
    /// real string seen so far, but is kept anyway as defensive symmetry with FirebirdDialect's
    /// identical try-base-then-legacy-regex ordering.
    /// </summary>
    public override Version? ParseVersion(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return null;
        }

        var standardVersion = base.ParseVersion(versionString);
        if (standardVersion != null)
        {
            return standardVersion;
        }

        var legacyMatch = Regex.Match(versionString, @"LI-V(\d+)\.(\d+)\.(\d+)");
        if (legacyMatch.Success &&
            int.TryParse(legacyMatch.Groups[1].Value, out var major) &&
            int.TryParse(legacyMatch.Groups[2].Value, out var minor) &&
            int.TryParse(legacyMatch.Groups[3].Value, out var build))
        {
            return new Version(major, minor, build);
        }

        return null;
    }
}
