// =============================================================================
// FILE: SybaseDialect.cs
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
internal class SybaseDialect : SqlDialect
{
    internal SybaseDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Sybase;
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Exec;
    public override bool PrepareStatements => false;

    // @@IDENTITY is per-connection safe (verified live).
    public override bool HasSessionScopedLastIdFunction() => true;

    protected override string GetNaturalKeySelectClause(string wrappedIdColumn) => $"SELECT TOP 1 {wrappedIdColumn}";
    protected override string GetNaturalKeyFirstRowOnlyClause() => string.Empty;

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

            select.Append(placeholder).Append(" AS ").Append(WrapObjectName(columns[i].Name));
        }

        select.Append(") AS s");
        return select.ToString();
    }

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

    // Verified live: a MERGE whose USING source is a multi-row VALUES-derived table
    // ("MERGE INTO t USING (VALUES (@b0, ...), (@b1, ...)) AS s(...) ON ...") fails with
    // "Incorrect syntax near the keyword 'VALUES'." ASE has no VALUES-derived-table-as-MERGE-source
    // support (unlike the single-row SELECT-derived USING source BuildUpsertMerge uses, which does
    // work — see SupportsMerge above). Falls back to one BuildUpdate container per entity instead,
    // the same safe fallback SQLite/MySQL/MariaDB/Firebird/Spanner use.
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
    public override string GetBaseSessionSettings() => "SET QUOTED_IDENTIFIER ON";

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
            2601 or 546 or 548 or 233 => DbErrorCategory.ConstraintViolation,
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
            546 => DbConstraintKind.ForeignKey,
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
}
