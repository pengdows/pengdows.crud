using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.dialects;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

internal static class InternalSqlDialectExtensions
{
    internal static void ApplyConnectionSettings(this ISqlDialect dialect, IDbConnection connection,
        IDatabaseContext context, bool readOnly)
    {
        GetInternal(dialect).ApplyConnectionSettings(connection, context, readOnly);
    }

    internal static string GetDatabaseVersion(this ISqlDialect dialect, ITrackedConnection connection)
    {
        return GetInternal(dialect).GetDatabaseVersion(connection);
    }

    internal static bool ShouldDisablePrepareOn(this ISqlDialect dialect, Exception ex)
    {
        return GetInternal(dialect).ShouldDisablePrepareOn(ex);
    }

    internal static void TryEnterReadOnlyTransaction(this ISqlDialect dialect, ITransactionContext transaction)
    {
        GetInternal(dialect).TryEnterReadOnlyTransaction(transaction);
    }

    internal static ValueTask TryEnterReadOnlyTransactionAsync(this ISqlDialect dialect,
        ITransactionContext transaction, CancellationToken cancellationToken = default)
    {
        return GetInternal(dialect).TryEnterReadOnlyTransactionAsync(transaction, cancellationToken);
    }

    internal static void InitializeUnknownProductInfo(this ISqlDialect dialect)
    {
        GetInternal(dialect).InitializeUnknownProductInfo();
    }

    internal static Version? ParseVersion(this ISqlDialect dialect, string versionString)
    {
        return GetInternal(dialect).ParseVersion(versionString);
    }

    internal static int? GetMajorVersion(this ISqlDialect dialect, string versionString)
    {
        return GetInternal(dialect).GetMajorVersion(versionString);
    }

    internal static DataTable GetDataSourceInformationSchema(this ISqlDialect dialect, ITrackedConnection connection)
    {
        return GetInternal(dialect).GetDataSourceInformationSchema(connection);
    }

    internal static bool IsReadCommittedSnapshotOn(this ISqlDialect dialect, ITrackedConnection connection)
    {
        return GetInternal(dialect).IsReadCommittedSnapshotOn(connection);
    }

    internal static bool IsSnapshotIsolationOn(this ISqlDialect dialect, ITrackedConnection connection)
    {
        return GetInternal(dialect).IsSnapshotIsolationOn(connection);
    }

    internal static Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(this ISqlDialect dialect,
        ITrackedConnection connection)
    {
        return GetInternal(dialect).DetectDatabaseInfoAsync(connection);
    }

    internal static IDatabaseProductInfo DetectDatabaseInfo(this ISqlDialect dialect, ITrackedConnection connection)
    {
        return GetInternal(dialect).DetectDatabaseInfo(connection);
    }

    internal static string RenderJsonArgument(this ISqlDialect dialect, string parameterMarker, IColumnInfo column)
    {
        return GetInternal(dialect).RenderJsonArgument(parameterMarker, column);
    }

    internal static void TryMarkJsonParameter(this ISqlDialect dialect, DbParameter parameter, IColumnInfo column)
    {
        GetInternal(dialect).TryMarkJsonParameter(parameter, column);
    }

    /// <summary>
    /// The gateways' multi-row insert with column metadata (<see cref="SqlDialect"/>'s internal
    /// overload); a dialect that isn't a <see cref="SqlDialect"/> gets the public builder.
    /// </summary>
    internal static void BuildBatchInsertSql(this ISqlDialect dialect, string tableName,
        IReadOnlyList<string> columnNames, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue,
        IReadOnlyList<IColumnInfo> columns)
    {
        if (dialect is SqlDialect sqlDialect)
        {
            sqlDialect.BuildBatchInsertSql(tableName, columnNames, rowCount, query, getValue, columns);
            return;
        }

        dialect.BuildBatchInsertSql(tableName, columnNames, rowCount, query, getValue);
    }

    /// <summary>
    /// The gateways' batch update with column metadata (key columns, then updated columns).
    /// </summary>
    internal static void BuildBatchUpdateSql(this ISqlDialect dialect, string tableName,
        IReadOnlyList<string> columnNames, IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query,
        Func<int, int, object?>? getValue, IReadOnlyList<IColumnInfo> columns)
    {
        if (dialect is SqlDialect sqlDialect)
        {
            sqlDialect.BuildBatchUpdateSql(tableName, columnNames, keyColumns, rowCount, query, getValue, columns);
            return;
        }

        dialect.BuildBatchUpdateSql(tableName, columnNames, keyColumns, rowCount, query, getValue);
    }

    internal static void MarkColumnParameter(this ISqlDialect dialect, DbParameter parameter, IColumnInfo column)
    {
        GetInternal(dialect).MarkColumnParameter(parameter, column);
    }

    internal static bool RendersColumnArgument(this ISqlDialect dialect, IColumnInfo column)
    {
        return GetInternal(dialect).RendersColumnArgument(column);
    }

    internal static string RenderColumnArgument(this ISqlDialect dialect, string parameterMarker, IColumnInfo column)
    {
        return GetInternal(dialect).RenderColumnArgument(parameterMarker, column);
    }

    /// <summary>
    /// True when an insert of these columns takes its values from a SELECT rather than VALUES
    /// (<see cref="SqlDialect.AllowsColumnArgumentsInValues"/>).
    /// </summary>
    internal static bool InsertsFromSelect(this ISqlDialect dialect, IReadOnlyList<IColumnInfo> columns)
    {
        return dialect is SqlDialect sqlDialect && sqlDialect.InsertsFromSelect(columns);
    }

    internal static string RenderMergeSource(this ISqlDialect dialect, IReadOnlyList<IColumnInfo> columns,
        IReadOnlyList<string> parameterNames)
    {
        return GetInternal(dialect).RenderMergeSource(columns, parameterNames);
    }

    internal static bool MergeBindsValuesDirectly(this ISqlDialect dialect) =>
        dialect is SqlDialect { MergeBindsValuesDirectly: true };

    /// <summary>The MERGE source for an upsert into <paramref name="tableName"/> (wrapped).</summary>
    internal static string RenderMergeSource(this ISqlDialect dialect, IReadOnlyList<IColumnInfo> columns,
        IReadOnlyList<string> parameterNames, string tableName)
    {
        return dialect is SqlDialect sqlDialect
            ? sqlDialect.RenderMergeSource(columns, parameterNames, tableName)
            : GetInternal(dialect).RenderMergeSource(columns, parameterNames);
    }

    /// <summary>
    /// See <see cref="IInternalSqlDialect.MergeMatchedConditionAsUpdateWhere"/>; false for a
    /// dialect that isn't an internal one (e.g. a test double).
    /// </summary>
    internal static bool MergeMatchedConditionAsUpdateWhere(this ISqlDialect dialect)
    {
        return dialect is IInternalSqlDialect internalDialect && internalDialect.MergeMatchedConditionAsUpdateWhere;
    }

    /// <summary>
    /// See <see cref="IInternalSqlDialect.SupportsMergeMatchedCondition"/>; true for a dialect that
    /// isn't an internal one (e.g. a test double).
    /// </summary>
    internal static bool SupportsMergeMatchedCondition(this ISqlDialect dialect)
    {
        return dialect is not IInternalSqlDialect internalDialect || internalDialect.SupportsMergeMatchedCondition;
    }

    /// <summary>
    /// See <see cref="IInternalSqlDialect.EmitsAnsiMergeSyntax"/>; true for a dialect that isn't an
    /// internal one (e.g. a test double).
    /// </summary>
    internal static bool EmitsAnsiMergeSyntax(this ISqlDialect dialect)
    {
        return dialect is not IInternalSqlDialect internalDialect || internalDialect.EmitsAnsiMergeSyntax;
    }

    /// <summary>
    /// See <see cref="IInternalSqlDialect.SupportsPureKeyUpsert"/>; false for a dialect that isn't an
    /// internal one (e.g. a test double).
    /// </summary>
    internal static bool SupportsPureKeyUpsert(this ISqlDialect dialect)
    {
        return dialect is IInternalSqlDialect internalDialect && internalDialect.SupportsPureKeyUpsert;
    }

    /// <summary>
    /// See <see cref="IInternalSqlDialect.RequiresOutputParameterForReturning"/>; false for a dialect
    /// that isn't an internal one (e.g. a test double).
    /// </summary>
    internal static bool RequiresOutputParameterForReturning(this ISqlDialect dialect)
    {
        return dialect is IInternalSqlDialect internalDialect && internalDialect.RequiresOutputParameterForReturning;
    }

    /// <summary>
    /// See <see cref="IInternalSqlDialect.MergeUpsertReportsSkippedVersionRow"/>; true for a dialect
    /// that isn't an internal one (e.g. a test double).
    /// </summary>
    internal static bool MergeUpsertReportsSkippedVersionRow(this ISqlDialect dialect)
    {
        return dialect is not IInternalSqlDialect internalDialect || internalDialect.MergeUpsertReportsSkippedVersionRow;
    }

    /// <summary>
    /// See <see cref="IInternalSqlDialect.PreservesEmptyBinary"/>; true for a dialect that isn't an
    /// internal one (e.g. a test double).
    /// </summary>
    internal static bool PreservesEmptyBinary(this ISqlDialect dialect)
    {
        return dialect is not IInternalSqlDialect internalDialect || internalDialect.PreservesEmptyBinary;
    }

    /// <summary>
    /// See <see cref="IInternalSqlDialect.SupportsSupplementaryCharacters"/>; true for a dialect that
    /// isn't an internal one (e.g. a test double).
    /// </summary>
    internal static bool SupportsSupplementaryCharacters(this ISqlDialect dialect)
    {
        return dialect is not IInternalSqlDialect internalDialect || internalDialect.SupportsSupplementaryCharacters;
    }

    /// <summary>
    /// See <see cref="IInternalSqlDialect.SupportsSpacesInIdentifiers"/>; true for a dialect that
    /// isn't an internal one (e.g. a test double).
    /// </summary>
    internal static bool SupportsSpacesInIdentifiers(this ISqlDialect dialect)
    {
        return dialect is not IInternalSqlDialect internalDialect || internalDialect.SupportsSpacesInIdentifiers;
    }

    /// <summary>
    /// See <see cref="IInternalSqlDialect.SupportsOnConflictOnSecondaryUniqueKey"/>; true for a
    /// dialect that isn't an internal one (e.g. a test double).
    /// </summary>
    internal static bool SupportsOnConflictOnSecondaryUniqueKey(this ISqlDialect dialect)
    {
        return dialect is not IInternalSqlDialect internalDialect ||
               internalDialect.SupportsOnConflictOnSecondaryUniqueKey;
    }

    /// <summary>
    /// See <see cref="IInternalSqlDialect.QualifiesColumnReferences"/>; false for a dialect that
    /// isn't an internal one (e.g. a test double).
    /// </summary>
    internal static bool QualifiesColumnReferences(this ISqlDialect dialect)
    {
        return dialect is IInternalSqlDialect internalDialect && internalDialect.QualifiesColumnReferences;
    }

    private static IInternalSqlDialect GetInternal(ISqlDialect dialect)
    {
        if (dialect is not IInternalSqlDialect internalDialect)
        {
            throw new InvalidOperationException("ISqlDialect must support internal detection operations.");
        }

        return internalDialect;
    }
}
