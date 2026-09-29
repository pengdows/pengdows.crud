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
//   interchangeable with '...' string literals. CONFIRMED live: a column literally named "user"
//   still can't be selected via its quoted identifier — even with Delimident=true, Informix
//   treats USER as a special register (like CURRENT/TODAY), not a plain reserved word quoting
//   can disambiguate.
// - Pagination: CONFIRMED live that OFFSET/FETCH and LIMIT m OFFSET n are both rejected, so
//   neither syntax flag is claimed. AppendPaging inserts the native "SKIP n FIRST m" directly
//   after the leading SELECT (SupportsPaging = true).
// - MERGE: CONFIRMED live with a one-row "USING (SELECT ... FROM sysmaster:sysdual) s" source
//   (the base "USING (VALUES (...)) AS s (...)" shape is rejected). Informix MERGE has no
//   conditional matched clause, so upsert of a [Version] entity is refused.
// - Savepoints: CONFIRMED live (SAVEPOINT / ROLLBACK TO SAVEPOINT / RELEASE SAVEPOINT).
// - Batch insert: CONFIRMED live that the ANSI multi-row VALUES clause
//   ("INSERT INTO t (...) VALUES (...), (...)") is rejected — Informix only accepts one row per
//   VALUES clause. Falls back to one INSERT per entity (SupportsBatchInsert = false).
// - Binary/BLOB parameter binding: CONFIRMED live that binding a parameter directly to a
//   BLOB/TEXT/BYTE column in an ordinary immediate INSERT is rejected ("Illegal attempt to use
//   Text/Byte host variable.") — a long-documented Informix ESQL/CLI restriction requiring an
//   INSERT cursor or locator-based (data-at-execution) binding, which pengdows.crud's parameter
//   binding doesn't implement.
// - ProcWrappingStyle: Informix -> "EXECUTE PROCEDURE proc(args)", the documented stand-alone form
//   (CALL is documented as SPL-only). CONFIRMED live for procedures with and without RETURNING and
//   for CREATE FUNCTION routines, for reads and writes alike.
// - Read-only transaction enforcement: CONFIRMED LIVE against a real
//   icr.io/informix/informix-developer-database container. "SET TRANSACTION READ ONLY" issued
//   inside an active transaction (BEGIN WORK, or a real ADO.NET conn.BeginTransaction() —
//   both tested) is accepted, and a subsequent write then fails with "Invalid operation for a
//   READ-ONLY transaction." Issued standalone (no active transaction) it instead fails with
//   "Not in transaction." — not a rejection of the statement itself, just confirming it must
//   run inside a transaction, which is always true when pengdows.crud calls
//   TryEnterReadOnlyTransaction (only ever invoked from within an already-open
//   TransactionContext). Implemented via the same TryExecuteReadOnlySql/TryExecuteReadOnlySqlAsync
//   shared helper OracleDialect uses for its own identical SET TRANSACTION READ ONLY support.
// - ApplicationName/pool discriminator: CONFIRMED via reflection against a real, live-connected
//   IfxConnectionStringBuilder (51 properties) that no ApplicationName-equivalent keyword
//   exists. Two connection-string candidates were found and confirmed to CONNECT successfully
//   (Optofc=1, DelimIdent=true), but neither was confirmed BEHAVIORALLY INERT — DelimIdent is
//   already part of every connection string this dialect builds (wouldn't differentiate
//   reader/writer pools at all), and Optofc ("Optimize Open Cursor") is a real CSDK
//   cursor-handling switch whose actual effect was not verified. "Connects without error" is
//   not the same bar as "confirmed inert" (the distinction Access's original
//   SupportsExternalPooling/PoolingSettingName bug blurred) — deliberately left unimplemented
//   rather than guessing. ReadOnlyPoolDiscriminatorSettingName stays at the SqlDialect base
//   default (null); reader and writer connections share one physical pool for now. See
//   docs/connection/new-database-pooling-appname-readonly-audit.md's Informix section.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Globalization;
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
/// genuinely unsupported). See <c>InformixDialectTests.cs</c> for the dedicated capability-flag
/// and exception-classification test coverage (CLAUDE.md's "Adding a New Database" checklist
/// item 7).
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

    // CONFIRMED live (15.0.1.0.3): MERGE INTO t USING (SELECT ... FROM sysmaster:sysdual) s
    // ON t.k = s.k WHEN MATCHED THEN UPDATE SET ... WHEN NOT MATCHED THEN INSERT ... works. The base
    // "USING (VALUES (...)) AS s (...)" derived table is a syntax error, so RenderMergeSource uses a
    // one-row SELECT from sysmaster:sysdual (Informix's DUAL). Informix MERGE has no conditional
    // matched clause (neither "WHEN MATCHED AND" nor "UPDATE ... WHERE" parses), so
    // SupportsMergeMatchedCondition is false and upsert of a [Version] entity is refused.
    public override bool SupportsMerge => true;
    public override bool SupportsMergeMatchedCondition => false;

    public override string UpsertIncomingColumn(string columnName)
    {
        return $"s.{WrapObjectName(columnName)}";
    }

    public override string RenderMergeSource(IReadOnlyList<IColumnInfo> columns,
        IReadOnlyList<string> parameterNames)
    {
        if (columns == null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        if (parameterNames == null)
        {
            throw new ArgumentNullException(nameof(parameterNames));
        }

        if (columns.Count != parameterNames.Count)
        {
            throw new ArgumentException("Column and parameter counts must match.");
        }

        var select = new System.Text.StringBuilder("USING (SELECT ");
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                select.Append(", ");
            }

            var placeholder = MakeParameterName(parameterNames[i]);
            if (columns[i].IsJsonType)
            {
                placeholder = RenderJsonArgument(placeholder, columns[i]);
            }

            select.Append("CAST(").Append(placeholder).Append(" AS ")
                .Append(GetMergeSourceCastType(columns[i].IsJsonType ? DbType.String : columns[i].DbType))
                .Append(") AS ").Append(WrapObjectName(columns[i].Name));
        }

        select.Append(" FROM sysmaster:sysdual) s");
        return select.ToString();
    }

    // CONFIRMED live (15.0.1.0.3, Informix.Net.Core): an untyped "? AS col" in the MERGE source's
    // select list is a syntax error; each placeholder needs "CAST(? AS type)". Every mapping here
    // was round-tripped through MERGE insert + update. Booleans are bound as Int16 on this
    // positional dialect (common conversions), hence SMALLINT. Types not verified live throw
    // rather than guess (binary cannot be bound as an ordinary parameter on Informix at all).
    internal static string GetMergeSourceCastType(DbType dbType) => dbType switch
    {
        DbType.Int64 => "BIGINT",
        DbType.Int32 => "INT",
        DbType.Int16 or DbType.Boolean => "SMALLINT",
        DbType.String or DbType.AnsiString or DbType.StringFixedLength or DbType.AnsiStringFixedLength
            or DbType.Guid => "LVARCHAR(32739)",
        DbType.Decimal => "DECIMAL(32)",
        DbType.Double => "FLOAT",
        DbType.Single => "SMALLFLOAT",
        DbType.DateTime or DbType.DateTime2 or DbType.DateTimeOffset => "DATETIME YEAR TO FRACTION(5)",
        DbType.Date => "DATE",
        _ => throw new NotSupportedException(
            $"Informix MERGE upsert does not support a {dbType} column in the source row.")
    };

    // CONFIRMED live (15.0.1.0.3): both "OFFSET n ROWS FETCH NEXT m ROWS ONLY" and
    // "LIMIT m OFFSET n" are syntax errors, so neither syntax flag is claimed. Informix pages with
    // its native "SELECT SKIP n FIRST m ..." (also confirmed live, including ahead of DISTINCT),
    // which AppendPaging inserts directly after the leading SELECT keyword.
    public override bool SupportsOffsetFetch => false;
    public override bool SupportsLimitOffset => false;
    public override bool SupportsPaging => true;

    // CONFIRMED live: the server stores trailing blanks (OCTET_LENGTH counts them), but
    // Informix.Net.Core trims them from every VARCHAR/NVARCHAR/LVARCHAR value it returns,
    // regardless of LeaveTrailingSpaces. IBM APAR IC63704: no option exists to disable it.
    public override bool PreservesTrailingWhitespace => false;

    // CONFIRMED live (15.0.1.0.3, DELIMIDENT): an unqualified quoted "user", "today", "current",
    // "sitename", "dbservername" or "current_user" in an expression resolves to the special
    // register, not the column - DELETE FROM "t" WHERE "user" = 'informix' deleted every row. The
    // table-qualified "t"."user" resolves to the column, so the gateways qualify every reference.
    public override bool QualifiesColumnReferences => true;

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

        var sql = query.ToString();
        var start = 0;
        while (start < sql.Length && char.IsWhiteSpace(sql[start]))
        {
            start++;
        }

        const string select = "SELECT";
        var afterSelect = start + select.Length;
        if (string.Compare(sql, start, select, 0, select.Length, StringComparison.OrdinalIgnoreCase) != 0
            || afterSelect >= sql.Length || !char.IsWhiteSpace(sql[afterSelect]))
        {
            throw new NotSupportedException(
                "Informix paging (SKIP/FIRST) must follow the query's leading SELECT keyword; " +
                "the query does not start with SELECT.");
        }

        var clause = offset > 0
            ? string.Create(CultureInfo.InvariantCulture, $" SKIP {offset} FIRST {limit}")
            : string.Create(CultureInfo.InvariantCulture, $" FIRST {limit}");
        query.Clear().Append(sql.AsSpan(0, afterSelect)).Append(clause).Append(sql.AsSpan(afterSelect));
    }

    // CONFIRMED live (15.0.1.0.3, logged database): SAVEPOINT, ROLLBACK TO SAVEPOINT and
    // RELEASE SAVEPOINT all work with quoted names, i.e. the base SqlDialect SQL unchanged.
    public override bool SupportsSavepoints => true;

    // CONFIRMED live: Informix rejects the ANSI SQL multi-row VALUES clause outright
    // ("ERROR [42000] ... A syntax error has occurred.") — Informix's INSERT statement only
    // accepts a single row per VALUES clause. Falls back to one BuildCreate per entity, the
    // same safe path SQLite/MySQL/MariaDB/Firebird already use for the same reason.
    public override bool SupportsBatchInsert => false;

    // EXECUTE PROCEDURE name(args): Informix's documented stand-alone statement (CALL is only valid
    // inside an SPL routine per the 12.10/14.10 docs, even though 15.0 happens to accept it). See
    // InformixProcWrappingStrategy for the live-verified details.
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Informix;

    // ANSI SQLSTATE 23000, or Informix's own numeric error codes for duplicate key: -268
    // (logged database) / -239 (unlogged database). Both are documented, distinct codes for
    // the SAME condition depending on database logging mode, not alternates for different
    // constraint kinds. Source: IBM support "SQL state X23000:-239".
    // SQLSTATE 23000 alone is not enough: Informix reports 23000 for every integrity violation
    // (check -530, not null -391, foreign key -691/-692; confirmed live), so it only decides when
    // no Informix error code is available (none, or only the exception's HRESULT).
    public override bool IsUniqueViolation(DbException ex) =>
        TryGetProviderErrorCode(ex) is { } code && code != ex.HResult
            ? Math.Abs(code) is 268 or 239
            : string.Equals(TryGetProviderSqlState(ex), "23000", StringComparison.OrdinalIgnoreCase);

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

    public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        // CONFIRMED live (testbed): Informix.Net.Core has no DbType.DateTimeOffset mapping
        // ("No mapping exists from DbType DateTimeOffset to a known IfxType", thrown from
        // IfxParameter.set_DbType before any later conversion can run), and Informix has no
        // offset-aware temporal type. Store the UTC instant as a plain DateTime, matching
        // Db2/Sybase/Firebird/InterBase. Null is remapped too: the driver rejects the DbType itself.
        if (type == DbType.DateTimeOffset)
        {
            object coerced = value is DateTimeOffset dto
                ? DateTime.SpecifyKind(dto.UtcDateTime, DateTimeKind.Unspecified)
                : DBNull.Value;
            return base.CreateDbParameter<object?>(name, DbType.DateTime, coerced);
        }

        // INTEGER's and SMALLINT's smallest value is their NULL representation (IBM docs: ranges
        // -2147483647..2147483647 and -32767..32767), so the driver refuses to bind int.MinValue as
        // an Int32 at all ("Error in assignment", confirmed live even for a BIGINT column). Bind those
        // valid CLR values one size wider; a column too narrow then gets the server's range error.
        if (type == DbType.Int32 && value is int i && i == int.MinValue)
        {
            return base.CreateDbParameter<object?>(name, DbType.Int64, (long)i);
        }

        if (type == DbType.Int16 && value is short s && s == short.MinValue)
        {
            return base.CreateDbParameter<object?>(name, DbType.Int32, (int)s);
        }

        return base.CreateDbParameter(name, type, value);
    }

    // CONFIRMED live (Informix 15 developer image, Informix.Net.Core, DB_LOCALE and CLIENT_LOCALE
    // en_US.utf8): CJK and other BMP text round-trips; any supplementary-plane character (an emoji)
    // fails with "An illegal character has been found in the statement".
    public override bool SupportsSupplementaryCharacters => false;

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

    // CONFIRMED LIVE (2026-09-18) against a real icr.io/informix/informix-developer-database
    // container: "SET TRANSACTION READ ONLY" issued inside an active transaction genuinely
    // enforces read-only — a subsequent write fails with "Invalid operation for a READ-ONLY
    // transaction.", confirmed both via raw BEGIN WORK/SQL text and via a real ADO.NET
    // conn.BeginTransaction(). Outside an active transaction it fails with "Not in transaction"
    // instead — matching the requirement that pengdows.crud only calls this from within
    // TransactionContext's already-open transaction, so that's never reached in practice. Same
    // mechanism (and same shared helper) OracleDialect uses for its own confirmed
    // SET TRANSACTION READ ONLY support.
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
    // inspected — see docs/connection/new-database-pooling-appname-readonly-audit.md). An
    // earlier pass this session found two candidates that CONNECT successfully (Optofc=1,
    // DelimIdent=true) but correctly declined both: DelimIdent is already part of every
    // connection string this dialect builds (wouldn't differentiate pools), and Optofc's actual
    // cursor-handling effect was unconfirmed — a property that merely "connects without error"
    // is not the same bar as "confirmed behaviorally inert" (the distinction Access's original
    // Pooling=True bug blurred).
    //
    // RESOLVED (2026-09-19, live against a real icr.io/informix/informix-developer-database
    // container): systematically dumped every property's own compiled-in default and tested
    // which ones can be set explicitly, live, without error — three connect successfully with
    // their value set to their OWN driver default (guaranteed inert by construction, the same
    // pattern InterBaseDialect's "fetch size=200" fix uses): Exclusive=no, MaxPoolSize=100, and
    // LeaveTrailingSpaces=False. MaxPoolSize was deliberately NOT chosen despite also qualifying:
    // ConnectionPoolingConfiguration.ApplyPoolDiscriminator skips setting the discriminator key
    // if the caller's own connection string already contains it, and MaxPoolSize is exactly the
    // kind of property a real caller is plausible to have already configured themselves —
    // silently defeating pool separation in precisely the case where a caller has customized
    // their own pooling. LeaveTrailingSpaces (a CHAR-column trailing-space read behavior flag)
    // is obscure enough that no real caller is expected to ever set it themselves, so it was
    // chosen over the otherwise-equally-valid Exclusive=no.
    internal override string? ReadOnlyPoolDiscriminatorSettingName => "LeaveTrailingSpaces";
    internal override string? ReadOnlyPoolDiscriminatorSettingValue => "False";
}
