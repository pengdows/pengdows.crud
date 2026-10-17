using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.Tests;

/// <summary>
/// An <see cref="ISqlContainer"/> that is not the library's <c>SqlContainer</c>, for the tests of code that must treat any
/// implementer correctly (the extension methods' fallback to the interface). Every Execute* member records its call and
/// returns the value the test scripted; everything else is unscripted and says so. A hand-written stub, not a mock: there is
/// no setup language and nothing to verify except the recorded calls.
/// </summary>
internal sealed class ScriptedSqlContainer : ISqlContainer
{
    public int NonQueryResult { get; set; }
    public object? ScalarResult { get; set; }
    public ITrackedReader? ReaderResult { get; set; }

    /// <summary>One entry per Execute* call: the member, the execution type if any, the command type and the token.</summary>
    public List<string> Calls { get; } = new();

    public bool IsDisposed { get; private set; }

    private static NotSupportedException Unscripted(string member) =>
        new($"{nameof(ScriptedSqlContainer)} does not script {member}.");

    private void Record(string member, ExecutionType? executionType, CommandType commandType, CancellationToken token) =>
        Calls.Add($"{member}({(executionType is { } e ? e + ", " : "")}{commandType}, {(token.CanBeCanceled ? "token" : "none")})");

    private T Scalar<T>() => ScalarResult is null ? default! : (T)ScalarResult;

    // ---- the scripted execution members -----------------------------------------------------------------------------
    public ValueTask<int> ExecuteNonQueryAsync(CommandType commandType = CommandType.Text) =>
        ExecuteNonQueryAsync(commandType, CancellationToken.None);

    public ValueTask<int> ExecuteNonQueryAsync(CommandType commandType, CancellationToken cancellationToken)
    {
        Record(nameof(ExecuteNonQueryAsync), null, commandType, cancellationToken);
        return new ValueTask<int>(NonQueryResult);
    }

    public ValueTask<int> ExecuteNonQueryAsync(ExecutionType executionType, CommandType commandType = CommandType.Text) =>
        ExecuteNonQueryAsync(executionType, commandType, CancellationToken.None);

    public ValueTask<int> ExecuteNonQueryAsync(ExecutionType executionType, CommandType commandType,
        CancellationToken cancellationToken)
    {
        Record(nameof(ExecuteNonQueryAsync), executionType, commandType, cancellationToken);
        return new ValueTask<int>(NonQueryResult);
    }

    public ValueTask<T> ExecuteScalarRequiredAsync<T>(CommandType commandType = CommandType.Text) =>
        ExecuteScalarRequiredAsync<T>(commandType, CancellationToken.None);

    public ValueTask<T> ExecuteScalarRequiredAsync<T>(CommandType commandType, CancellationToken cancellationToken)
    {
        Record(nameof(ExecuteScalarRequiredAsync), null, commandType, cancellationToken);
        return new ValueTask<T>(Scalar<T>());
    }

    public ValueTask<T> ExecuteScalarRequiredAsync<T>(ExecutionType executionType,
        CommandType commandType = CommandType.Text) =>
        ExecuteScalarRequiredAsync<T>(executionType, commandType, CancellationToken.None);

    public ValueTask<T> ExecuteScalarRequiredAsync<T>(ExecutionType executionType, CommandType commandType,
        CancellationToken cancellationToken)
    {
        Record(nameof(ExecuteScalarRequiredAsync), executionType, commandType, cancellationToken);
        return new ValueTask<T>(Scalar<T>());
    }

    public ValueTask<T?> ExecuteScalarOrNullAsync<T>(CommandType commandType = CommandType.Text) =>
        ExecuteScalarOrNullAsync<T>(commandType, CancellationToken.None);

    public ValueTask<T?> ExecuteScalarOrNullAsync<T>(CommandType commandType, CancellationToken cancellationToken)
    {
        Record(nameof(ExecuteScalarOrNullAsync), null, commandType, cancellationToken);
        return new ValueTask<T?>(ScalarResult is null ? default : (T)ScalarResult);
    }

    public ValueTask<T?> ExecuteScalarOrNullAsync<T>(ExecutionType executionType,
        CommandType commandType = CommandType.Text) =>
        ExecuteScalarOrNullAsync<T>(executionType, commandType, CancellationToken.None);

    public ValueTask<T?> ExecuteScalarOrNullAsync<T>(ExecutionType executionType, CommandType commandType,
        CancellationToken cancellationToken)
    {
        Record(nameof(ExecuteScalarOrNullAsync), executionType, commandType, cancellationToken);
        return new ValueTask<T?>(ScalarResult is null ? default : (T)ScalarResult);
    }

    public ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(CommandType commandType = CommandType.Text) =>
        TryExecuteScalarAsync<T>(commandType, CancellationToken.None);

    public ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(CommandType commandType, CancellationToken cancellationToken)
    {
        Record(nameof(TryExecuteScalarAsync), null, commandType, cancellationToken);
        return new ValueTask<ScalarResult<T>>(new ScalarResult<T>(
            ScalarResult is null ? ScalarStatus.Null : ScalarStatus.Value, ScalarResult is null ? default : (T)ScalarResult));
    }

    public ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(ExecutionType executionType,
        CommandType commandType = CommandType.Text) =>
        TryExecuteScalarAsync<T>(executionType, commandType, CancellationToken.None);

    public ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(ExecutionType executionType, CommandType commandType,
        CancellationToken cancellationToken)
    {
        Record(nameof(TryExecuteScalarAsync), executionType, commandType, cancellationToken);
        return new ValueTask<ScalarResult<T>>(new ScalarResult<T>(
            ScalarResult is null ? ScalarStatus.Null : ScalarStatus.Value, ScalarResult is null ? default : (T)ScalarResult));
    }

    public ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType commandType = CommandType.Text) =>
        ExecuteReaderAsync(commandType, CancellationToken.None);

    public ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType commandType, CancellationToken cancellationToken)
    {
        Record(nameof(ExecuteReaderAsync), null, commandType, cancellationToken);
        return new ValueTask<ITrackedReader>(ReaderResult ?? throw Unscripted(nameof(ReaderResult)));
    }

    public ValueTask<ITrackedReader> ExecuteReaderAsync(ExecutionType executionType,
        CommandType commandType = CommandType.Text) =>
        ExecuteReaderAsync(executionType, commandType, CancellationToken.None);

    public ValueTask<ITrackedReader> ExecuteReaderAsync(ExecutionType executionType, CommandType commandType,
        CancellationToken cancellationToken)
    {
        Record(nameof(ExecuteReaderAsync), executionType, commandType, cancellationToken);
        return new ValueTask<ITrackedReader>(ReaderResult ?? throw Unscripted(nameof(ReaderResult)));
    }

    // ---- disposal ---------------------------------------------------------------------------------------------------
    public void Dispose() => IsDisposed = true;

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    // ---- everything else is unscripted ------------------------------------------------------------------------------
    public ISqlQueryBuilder Query => throw Unscripted(nameof(Query));
    public int ParameterCount => throw Unscripted(nameof(ParameterCount));
    public bool HasWhereAppended => throw Unscripted(nameof(HasWhereAppended));
    public string QuotePrefix => throw Unscripted(nameof(QuotePrefix));
    public string QuoteSuffix => throw Unscripted(nameof(QuoteSuffix));
    public string CompositeIdentifierSeparator => throw Unscripted(nameof(CompositeIdentifierSeparator));
    public string WrapObjectName(string name) => throw Unscripted(nameof(WrapObjectName));
    public string MakeParameterName(DbParameter dbParameter) => throw Unscripted(nameof(MakeParameterName));
    public string MakeParameterName(string parameterName) => throw Unscripted(nameof(MakeParameterName));
    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value) => throw Unscripted(nameof(CreateDbParameter));
    public DbParameter CreateDbParameter<T>(DbType type, T value) => throw Unscripted(nameof(CreateDbParameter));
    public void AddParameter(DbParameter parameter) => throw Unscripted(nameof(AddParameter));
    public DbParameter AddParameterWithValue<T>(DbType type, T value) => throw Unscripted(nameof(AddParameterWithValue));
    public DbParameter AddParameterWithValue<T>(string? name, DbType type, T value) => throw Unscripted(nameof(AddParameterWithValue));
    public DbParameter AddParameterWithValue<T>(DbType type, T value, ParameterDirection direction) => throw Unscripted(nameof(AddParameterWithValue));
    public DbParameter AddParameterWithValue<T>(string? name, DbType type, T value, ParameterDirection direction) => throw Unscripted(nameof(AddParameterWithValue));
    public void SetParameterValue(string parameterName, object? newValue) => throw Unscripted(nameof(SetParameterValue));
    public object? GetParameterValue(string parameterName) => throw Unscripted(nameof(GetParameterValue));
    public T GetParameterValue<T>(string parameterName) => throw Unscripted(nameof(GetParameterValue));
    public void AddParameters(IEnumerable<DbParameter> list) => throw Unscripted(nameof(AddParameters));
    public void AddParameters(IList<DbParameter> list) => throw Unscripted(nameof(AddParameters));
    public void Clear() => throw Unscripted(nameof(Clear));
    public string WrapForStoredProc(ExecutionType executionType, bool includeParameters = true, bool captureReturn = false) => throw Unscripted(nameof(WrapForStoredProc));
    public ISqlContainer Clone() => throw Unscripted(nameof(Clone));
    public ISqlContainer Clone(IDatabaseContext? context) => throw Unscripted(nameof(Clone));
}
