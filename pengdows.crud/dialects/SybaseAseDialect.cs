// =============================================================================
// FILE: SybaseAseDialect.cs
// PURPOSE: Sybase (SAP) Adaptive Server Enterprise dialect implementation.
//
// AI SUMMARY:
// - T-SQL family (shared heritage with SQL Server): @ parameter marker, EXEC proc
//   wrapping, SAVE TRANSACTION / ROLLBACK TRANSACTION savepoints.
// - Verified live against ASE 16.0 SP02 (nguoianphu/docker-sybase, AdoNetCore.AseClient
//   0.19.2) rather than assumed from SQL Server parity — several details differ:
//     * MERGE is supported but rejects a trailing semicolon ("Incorrect syntax near ';'").
//     * No OUTPUT/RETURNING clause; generated keys use @@IDENTITY as a compound
//       statement, space-separated (a trailing semicolon before it is also rejected).
//     * No OFFSET/FETCH or LIMIT/OFFSET; paging requires a statement-level
//       SET ROWCOUNT n / SET ROWCOUNT 0 pair that cannot be expressed as a suffix.
//     * OBJECT_ID() only accepts the single-argument form in this build.
//     * No CTEs or window functions even at 16.0 — MaxSupportedStandard is kept at
//       Sql92 so the generic capability gates in the base class do not over-claim.
// - AseException (AdoNetCore.AseClient) does not derive from DbException and has no
//   top-level error-code property; the real ASE error number lives on
//   AseException.Errors[0].MessageNumber. DbExceptionTranslationSupport.TryGetErrorCode
//   reflects over that shape as a fallback, and this dialect reuses it directly to
//   override the Exception-typed exception-analysis entry points (the DbException-typed
//   ones in the base class can never fire for AseException).
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.exceptions.translators;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Sybase (SAP) Adaptive Server Enterprise dialect.
/// </summary>
internal class SybaseAseDialect : SqlDialect
{
    // TYPE-002: AseClient writes a Guid to BINARY(16) in .NET's mixed-endian ToByteArray order.
    internal override bool StoresGuidBytesBigEndian => false;

    internal SybaseAseDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.SybaseASE;

    // Confirmed via reflection against AdoNetCore.AseClient.Internal.ConnectionParameters (the
    // driver's real connection-string parser — its public AseConnectionStringBuilder is a thin
    // DbConnectionStringBuilder wrapper exposing nothing usable): ApplicationName is a real
    // property there, defaulting to the current process name when unset. Without this set,
    // reader/writer connection strings would be identical and collapse into one shared pool.
    public override string? ApplicationNameSettingName => "ApplicationName";

    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Exec;
    public override bool PrepareStatements => false;

    // ASE has no CTEs or window functions even at 16.0; keep the standard-compliance
    // gate conservative so the base class's generic capability flags do not over-claim.
    public override Dictionary<int, SqlStandardLevel> GetMajorVersionToStandardMapping() => new();
    public override SqlStandardLevel GetDefaultStandardLevel() => SqlStandardLevel.Sql92;
    public override bool SupportsWindowFunctions => false;
    public override bool SupportsCommonTableExpressions => false;
    public override bool SupportsJsonTypes => false;
    public override bool SupportsXmlTypes => false;

    // Verified live: ASE's parser rejects a standalone VALUES table-value-constructor used as
    // a derived table ("Incorrect syntax near the keyword 'VALUES'") — the base class's
    // "USING (VALUES (@p0, @p1)) AS s (col0, col1)" MERGE source shape, unlike SQL Server.
    // "USING (SELECT @p0 AS col0, @p1 AS col1) AS s" is ANSI-standard and works live.
    public override string RenderMergeSource(IReadOnlyList<IColumnInfo> columns, IReadOnlyList<string> parameterNames)
    {
        return RenderSelectMergeSource(columns, parameterNames, "", " AS s");
    }

    // Verified live (ASE 16.0 SP02): the engine strips trailing blanks from VARCHAR values on
    // storage ('  padded  ' is stored as 8 bytes), so they cannot round-trip.
    public override bool PreservesTrailingWhitespace => false;

    // CONFIRMED live (ASE 16.0): a zero-length VARBINARY reads back as 0x00.
    public override bool PreservesEmptyBinary => false;

    // Guids pass through to AdoNetCore.AseClient (the SqlDialect default), as in 2.0.5. Verified
    // live (ASE 16.0 SP02): the driver writes DbType.Guid into BINARY(16)/VARBINARY(16) as
    // Guid.ToByteArray(), which reads back as the same Guid and matches in equality lookups - use
    // BINARY(16) columns for Guids on ASE. (Into a CHAR(36) column the driver writes those 16 bytes
    // as characters, which do not read back as a Guid; a string format was tried and reverted
    // because it broke the BINARY(16) columns 2.0.5 already handled correctly.)

    public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        if (IsDeclaredInstant(type, value, out var instant))
        {
            return CreateDbParameter(name, type, instant);
        }

        // Verified live (testbed): AdoNetCore.AseClient rejects a DateTimeOffset parameter
        // outright ("Unsupported .net type System.DateTimeOffset"), and ASE has no offset-aware
        // temporal type. Store the UTC instant as a plain DateTime, matching Db2/Firebird/
        // InterBase. Null is remapped too: the driver rejects DbType.DateTimeOffset regardless
        // of the value.
        // TYPE-022, confirmed live (ASE 16.0, AseClient 0.19.2): a typed DateTime is truncated to
        // milliseconds on the way in, so BIGDATETIME lost its microseconds. Microsecond text converts
        // implicitly into BIGDATETIME exactly and into DATETIME as a typed value would, in writes and
        // WHERE alike. Truncated, never rounded.
        if (type is DbType.DateTime2 or DbType.DateTimeOffset)
        {
            var text = TimestampText(value, BigDateTimeFormat);
            if (text != null)
            {
                return base.CreateDbParameter<object?>(name, DbType.String, text);
            }
        }

        // CONFIRMED live (ASE 16.0, AdoNetCore.AseClient 0.19.2): a NULL typed DbType.Boolean is sent
        // as BIT, which can't be NULL, and is stored as 0, so a nullable bool written as NULL read back
        // as false. A NULL typed Byte is stored as NULL. (A BIT column can't hold NULL at all; a
        // nullable bool column on ASE is TINYINT or similar.)
        if (type == DbType.Boolean && (value is null || value is DBNull))
        {
            return base.CreateDbParameter<object?>(name, DbType.Byte, DBNull.Value);
        }

        return base.CreateDbParameter(name, type, value);
    }

    private const string BigDateTimeFormat = "yyyy-MM-dd HH:mm:ss.ffffff";

    private static string BigTimeText(TimeSpan value) =>
        value.ToString(@"hh\:mm\:ss\.ffffff", System.Globalization.CultureInfo.InvariantCulture);

    // TYPE-022, confirmed live: BIGTIME refuses text without CONVERT ("Implicit conversion from
    // UNIVARCHAR to BIGTIME is not allowed"), and a typed TimeSpan loses its microseconds. The
    // gateways write a time column as CONVERT(BIGTIME, text), which a TIME column accepts as well.
    public override bool RendersColumnArgument(IColumnInfo column) =>
        column.DbType == DbType.Time || base.RendersColumnArgument(column);

    public override string RenderColumnArgument(string parameterMarker, IColumnInfo column) =>
        column.DbType == DbType.Time
            ? string.Concat("CONVERT(BIGTIME, ", parameterMarker, ")")
            : base.RenderColumnArgument(parameterMarker, column);

    internal override bool MarksColumnParameter(IColumnInfo column) =>
        column.DbType == DbType.Time || base.MarksColumnParameter(column);

    public override void MarkColumnParameter(DbParameter parameter, IColumnInfo column)
    {
        base.MarkColumnParameter(parameter, column);
        if (column.DbType != DbType.Time)
        {
            return;
        }

        var text = parameter.Value switch
        {
            TimeSpan span => BigTimeText(span),
            TimeOnly time => BigTimeText(time.ToTimeSpan()),
            _ => null
        };
        parameter.DbType = DbType.String;
        if (text != null)
        {
            parameter.Value = text;
        }
    }

    // TYPE-022, confirmed live: AseClient decodes BIGDATETIME a few microseconds off (.123456 as
    // .1229952) and can't read BIGTIME at all ("Unsupported data type 188"). Styles 140 and 137
    // render them exactly (yyyy-mm-dd hh:mm:ss.ffffff, hh:mm:ss.ffffff); DATETIME and TIME read the
    // same way.
    internal override string RenderColumnSelect(string columnReference, string wrappedName, IColumnInfo column) =>
        column.DbType switch
        {
            DbType.DateTime2 or DbType.DateTimeOffset => $"CONVERT(VARCHAR(26), {columnReference}, 140) AS {wrappedName}",
            DbType.Time => $"CONVERT(VARCHAR(15), {columnReference}, 137) AS {wrappedName}",
            _ => columnReference
        };

    // Verified live: this ASE build rejects the multi-row VALUES clause the base
    // implementation generates ("INSERT INTO t (...) VALUES (r1...), (r2...)") with
    // "Incorrect syntax near ','." — falls back to one INSERT per row.
    public override bool SupportsBatchInsert => false;

    // Verified live: SAVE TRANSACTION / ROLLBACK TRANSACTION work exactly as in SQL Server.
    public override bool SupportsSavepoints => true;

    // Same T-SQL family as SQL Server: SAVE TRANSACTION has no explicit release statement.
    public override SavepointCapabilities SavepointCapabilities =>
        SavepointCapabilities.Create | SavepointCapabilities.Rollback;

    public override string GetSavepointSql(string name)
    {
        return $"SAVE TRANSACTION {WrapObjectName(name)}";
    }

    public override string GetRollbackToSavepointSql(string name)
    {
        return $"ROLLBACK TRANSACTION {WrapObjectName(name)}";
    }

    // Verified live: MERGE works (both WHEN MATCHED / WHEN NOT MATCHED), but a trailing
    // semicolon is rejected ("Incorrect syntax near ';'"), unlike SQL Server.
    public override bool SupportsMerge => true;

    // CONFIRMED live (ASE 16.0): when "WHEN MATCHED AND t.version = s.version" is false the row is
    // left untouched, but @@rowcount (and the driver's rows affected) still reports 1, so a stale
    // [Version] upsert can't be told from a successful one.
    public override bool MergeUpsertReportsSkippedVersionRow => false;

    // Verified live: a MERGE whose USING source is a multi-row VALUES-derived table
    // ("MERGE INTO t USING (VALUES (@b0, ...), (@b1, ...)) AS s(...) ON ...") fails with
    // "Incorrect syntax near the keyword 'VALUES'." ASE has no VALUES-derived-table-as-MERGE-source
    // support (unlike the single-row SELECT-derived USING source RenderMergeSource uses, which
    // does work — see SupportsMerge above). Falls back to one BuildUpdate container per entity
    // instead, the same safe fallback SQLite/MySQL/MariaDB/Firebird use.
    public override bool SupportsBatchUpdate => false;

    // Verified live: "Incorrect syntax near ';'." when BuildUpsertMerge's generated MERGE
    // statement ends with a trailing semicolon — unlike SQL Server, ASE rejects it.
    public override bool RequiresMergeStatementTerminator => false;

    // Verified live: ASE rejects ';' as a multi-statement batch separator entirely (not just
    // as a trailing terminator) — "DECLARE @x INT; EXEC ...;" fails with "Incorrect syntax
    // near ';'." even though the equivalent newline-separated batch (no semicolons at all)
    // executes fine as one round trip.
    public override bool SupportsSemicolonStatementSeparator => false;

    // (No BuildBatchUpdateSql override: SupportsBatchUpdate is false above, so
    // TableGateway.BuildBatchUpdate never calls it. This dialect previously carried an override
    // here generating "MERGE INTO t USING (VALUES (...), (...)) AS s(...)" — the unsupported
    // VALUES-derived-table-as-MERGE-source construct documented above — removed rather than left
    // as unreachable, known-broken code.)

    // ASE has no OUTPUT/RETURNING clause. IDENTITY columns exist and @@IDENTITY works
    // (verified live), so generated keys are read back via a compound statement.
    public override bool SupportsInsertReturning => false;
    public override bool SupportsIdentityColumns => true;
    // CONFIRMED live 2026-09-29: the driver wraps a TimeSpan outside a day instead of rejecting it
    // (-01:00:00 was stored as 23:00:00), so SqlDialect rejects one before binding.
    internal override bool TimeColumnHoldsOnlyATimeOfDay => true;

    // CONFIRMED live 2026-09-30: AdoNetCore.AseClient decodes result rows inside ExecuteReader
    // (TokenReader → ValueReader.ReadTDS_DECN), so a NUMERIC above decimal.MaxValue throws
    // OverflowException there. No parameter-side overflow reaches that point on this driver.
    internal override bool DecodesResultRowsAtExecute => true;

    public override GeneratedKeyPlan GetGeneratedKeyPlan() => GeneratedKeyPlan.CompoundStatement;

    // Verified live: "INSERT ...; SELECT @@IDENTITY" (semicolon-separated) fails with
    // "Incorrect syntax near ';'"; "INSERT ... SELECT @@IDENTITY" (space-separated) works.
    public override string GetCompoundInsertIdSuffix() => " SELECT @@IDENTITY";
    public override string GetLastInsertedIdQuery() => "SELECT @@IDENTITY";

    // Enforces ANSI double-quote identifier support (matches the base class's QuotePrefix/
    // QuoteSuffix defaults, which this dialect deliberately does not override). Verified live:
    // ASE rejects a trailing semicolon after this statement when it is combined with anything
    // else in the same batch (see GetReadOnlyTransactionResetSql, left at the base "null"
    // default so GetFinalSessionSettings never tries to semicolon-join it with a second
    // statement) — ASE does not accept ';' as a multi-statement separator at all in this
    // provider/version, unlike every other T-SQL-family dialect in this codebase.
    //
    // SET ANSINULL ON: ASE's default (off) makes "col = @p" with a NULL parameter match NULL rows,
    // unlike every other database (confirmed live, ASE 16.0); this is the ASE counterpart of SQL
    // Server's ANSI_NULLS ON. Newline-separated: ASE accepts several statements in one batch but
    // not a ';' between them.
    public override string GetBaseSessionSettings() => "SET QUOTED_IDENTIFIER ON\nSET ANSINULL ON";

    public override string GetVersionQuery() => "SELECT @@version";

    public override string ExtractProductNameFromVersion(string versionString)
    {
        return versionString.Contains("Adaptive Server Enterprise", StringComparison.OrdinalIgnoreCase)
            ? "Sybase Adaptive Server Enterprise"
            : base.ExtractProductNameFromVersion(versionString);
    }

    // Verified live: ASE has neither OFFSET/FETCH nor LIMIT/OFFSET. Paging requires a
    // statement-level "SET ROWCOUNT n" issued before the query and "SET ROWCOUNT 0" after,
    // which cannot be expressed by appending a suffix to the existing query text — the
    // contract this method is built around. Overriding to throw is more honest than
    // emitting SQL that would fail against a real server.
    public override bool SupportsOffsetFetch => false;
    public override bool SupportsLimitOffset => false;

    public override void AppendPaging(ISqlQueryBuilder query, int offset, int limit)
    {
        throw new NotSupportedException(
            "Sybase ASE has no OFFSET/FETCH or LIMIT/OFFSET clause. Paging requires a " +
            "statement-level 'SET ROWCOUNT n' issued before the query (and 'SET ROWCOUNT 0' " +
            "afterward), which cannot be expressed by appending to the existing query text.");
    }

    // AseException (AdoNetCore.AseClient) does not derive from DbException and exposes no
    // top-level error-code property — only an "Errors" collection of AseError records with a
    // MessageNumber. The base class's IsUniqueViolation(DbException)/ClassifyException/
    // AnalyzeException are gated on "is DbException" and can never fire for it, so the
    // Exception-typed entry points are overridden here using the same duck-typed extraction
    // DbExceptionTranslationSupport already uses for the exception-translator path.
    public override bool IsUniqueViolation(Exception ex)
    {
        var number = DbExceptionTranslationSupport.TryGetErrorCode(ex);
        return number.HasValue ? number.Value == 2601 : base.IsUniqueViolation(ex);
    }

    public override DbErrorCategory ClassifyException(Exception exception)
    {
        var number = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        if (!number.HasValue)
        {
            return base.ClassifyException(exception);
        }

        return number.Value switch
        {
            2601 or 546 or 547 or 548 or 233 => DbErrorCategory.ConstraintViolation,
            1205 => DbErrorCategory.Deadlock,
            _ => base.ClassifyException(exception)
        };
    }

    public override DbExceptionInfo AnalyzeException(Exception exception)
    {
        var number = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        if (!number.HasValue)
        {
            return base.AnalyzeException(exception);
        }

        var category = ClassifyException(exception);
        var constraintKind = number.Value switch
        {
            2601 => DbConstraintKind.Unique,
            // 546: child row without a parent; 547: parent row still referenced (confirmed live).
            546 or 547 => DbConstraintKind.ForeignKey,
            233 => DbConstraintKind.NotNull,
            548 => DbConstraintKind.Check,
            _ => DbConstraintKind.None
        };
        var isDeadlock = number.Value == 1205;

        return new DbExceptionInfo(
            category,
            constraintKind,
            isDeadlock,
            isDeadlock,
            number,
            DbExceptionTranslationSupport.TryGetSqlState(exception));
    }

    // Isolation mapping (DEC-010; was IsolationResolver's per-database switch, same names as 3.0).
    internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) =>
        new HashSet<IsolationLevel>
        {
            IsolationLevel.ReadUncommitted,
            IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead,
            IsolationLevel.Serializable
        };

    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) =>
        new Dictionary<IsolationProfile, IsolationLevel>
        {
            [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.RepeatableRead,
            [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
            [IsolationProfile.FastWithRisks] = IsolationLevel.ReadUncommitted
        };

    // REV-039: the translator for this database's provider exceptions; no type mappings or
    // value formats of its own.
    internal static DatabaseTraits CreateSybaseAseTraits() =>
        new(SupportedDatabase.SybaseASE, new SybaseExceptionTranslator());
}
