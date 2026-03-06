using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

internal interface IInternalSqlDialect
{
    void ApplyConnectionSettings(IDbConnection connection, IDatabaseContext context, bool readOnly);

    bool ShouldDisablePrepareOn(Exception ex);

    void TryEnterReadOnlyTransaction(ITransactionContext transaction);

    ValueTask TryEnterReadOnlyTransactionAsync(ITransactionContext transaction,
        CancellationToken cancellationToken = default);

    void InitializeUnknownProductInfo();

    Version? ParseVersion(string versionString);

    int? GetMajorVersion(string versionString);

    string GetDatabaseVersion(ITrackedConnection connection);

    DataTable GetDataSourceInformationSchema(ITrackedConnection connection);

    bool IsReadCommittedSnapshotOn(ITrackedConnection connection);

    bool IsSnapshotIsolationOn(ITrackedConnection connection);

    Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection);

    IDatabaseProductInfo DetectDatabaseInfo(ITrackedConnection connection);
}
