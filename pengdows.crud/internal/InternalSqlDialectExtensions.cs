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

    internal static string RenderMergeSource(this ISqlDialect dialect, IReadOnlyList<IColumnInfo> columns,
        IReadOnlyList<string> parameterNames)
    {
        return GetInternal(dialect).RenderMergeSource(columns, parameterNames);
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
