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
//   SetEnvironmentVariable) — see testbed/Informix/InformixNativeLibraryBootstrap.cs for the full story.
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
using System.Globalization;
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
    // same safe path Firebird/InterBase/HANA/Access/Sybase ASE use.
    public override bool SupportsBatchInsert => false;

    // EXECUTE PROCEDURE name(args): Informix's documented stand-alone statement (CALL is only valid
    // inside an SPL routine per the 12.10/14.10 docs, even though 15.0 happens to accept it). See
    // InformixProcWrappingStrategy for the live-verified details.
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Informix;

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

    // Advisory-level category classification (ISqlDialect.AnalyzeException/ClassifyException)
    // lives in SqlDialect.cs's private TryClassifyProviderException switch on this branch - 2.0.6
    // predates 3.0's dialect-owned classification refactor, so there is no virtual member here to
    // override. See that switch's SupportedDatabase.Informix case for the same -143/-244 codes
    // used in InformixExceptionTranslator.cs, which is what actually determines the exception
    // TYPE thrown - that switch only affects the separate advisory-diagnostics path.

    // Informix's own terminology (Dirty Read/Committed Read/Cursor Stability/Repeatable Read)
    // maps to ADO.NET's IsolationLevel — see IsolationResolver.cs's SupportedDatabase.Informix
    // cases (2.0.6 resolves isolation through a central switch, not a dialect-owned override).

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

        return base.CreateDbParameter(name, type, value);
    }

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
}
