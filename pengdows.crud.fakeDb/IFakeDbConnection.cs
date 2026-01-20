#region

using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.fakeDb;

public interface IFakeDbConnection : IDbConnection, IAsyncDisposable
{
    SupportedDatabase EmulatedProduct { get; set; }

    int OpenCount { get; }

    int OpenAsyncCount { get; }

    int CloseCount { get; }

    int DisposeCount { get; }

    IReadOnlyCollection<IEnumerable<Dictionary<string, object>>> RemainingReaderResults { get; }

    IReadOnlyCollection<object?> RemainingScalarResults { get; }

    IReadOnlyCollection<int> RemainingNonQueryResults { get; }

    IReadOnlyCollection<string> ExecutedNonQueryTexts { get; }

    void EnqueueReaderResult(IEnumerable<Dictionary<string, object>> rows);

    void EnqueueScalarResult(object? value);

    void EnqueueNonQueryResult(int value);

    void SetScalarResultForCommand(string commandText, object? value);

    void SetServerVersion(string version);

    void SetMaxParameterLimit(int limit);

    int? GetMaxParameterLimit();

    void SetFailOnOpen(bool shouldFail = true, bool skipFirstOpen = false);

    void SetFailOnCommand(bool shouldFail = true);

    void SetFailOnBeginTransaction(bool shouldFail = true);

    void SetCustomFailureException(Exception exception);

    void SetFailAfterOpenCount(int openCount);

    void BreakConnection(bool skipFirst = false);

    void ResetFailureConditions();

    DataTable GetSchema();

    DataTable GetSchema(string meta);

    Task OpenAsync(CancellationToken cancellationToken);
}
