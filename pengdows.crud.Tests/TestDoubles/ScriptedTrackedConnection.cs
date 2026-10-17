using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.connection;
using pengdows.crud.threading;
using pengdows.crud.wrappers;

namespace pengdows.crud.Tests;

/// <summary>
/// An <see cref="ITrackedConnection"/> that is not the library's <c>TrackedConnection</c>, for code that must cope with any
/// implementer: one whose Dispose throws (a real TrackedConnection catches and logs that itself, so it cannot stand in), one
/// that fails to open, or one that counts how it was disposed. A hand-written stub, not a mock. Like the interface it does not
/// implement <see cref="IAsyncDisposable"/>, so disposal code takes its non-async path.
/// </summary>
internal class ScriptedTrackedConnection : ITrackedConnection
{
    public ConnectionState State { get; set; } = ConnectionState.Closed;
    private string _connectionString = "";

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = value ?? "";
    }

    /// <summary>Thrown by Open and OpenAsync when set.</summary>
    public Exception? OpenException { get; set; }

    /// <summary>Thrown by Dispose when set (after it is counted).</summary>
    public Exception? DisposeException { get; set; }

    /// <summary>Thrown by DisposeAsync when set (after it is counted).</summary>
    public Exception? DisposeAsyncException { get; set; }

    public int OpenCount { get; private set; }
    public int DisposeCount { get; private set; }
    public int DisposeAsyncCount { get; private set; }

    private static NotSupportedException Unscripted(string member) =>
        new($"{nameof(ScriptedTrackedConnection)} does not script {member}.");

    public void Open()
    {
        OpenCount++;
        if (OpenException != null)
        {
            throw OpenException;
        }

        State = ConnectionState.Open;
    }

    public ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        Open();
        return ValueTask.CompletedTask;
    }

    public void Close() => State = ConnectionState.Closed;

    public void Dispose()
    {
        DisposeCount++;
        if (DisposeException != null)
        {
            throw DisposeException;
        }
    }

    public ValueTask DisposeAsync()
    {
        DisposeAsyncCount++;
        if (DisposeAsyncException != null)
        {
            throw DisposeAsyncException;
        }

        return ValueTask.CompletedTask;
    }

    public int ConnectionTimeout => 15;
    public string Database => "scripted";
    public string DataSource => "scripted";
    public string ServerVersion => "1.0";
    public IConnectionLocalState LocalState => throw Unscripted(nameof(LocalState));
    public ILockerAsync GetLock() => throw Unscripted(nameof(GetLock));
    public IDbTransaction BeginTransaction() => throw Unscripted(nameof(BeginTransaction));
    public IDbTransaction BeginTransaction(IsolationLevel isolationLevel) => throw Unscripted(nameof(BeginTransaction));
    public ValueTask<IDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw Unscripted(nameof(BeginTransactionAsync));
    public ValueTask<IDbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default) => throw Unscripted(nameof(BeginTransactionAsync));
    public void ChangeDatabase(string databaseName) => throw Unscripted(nameof(ChangeDatabase));
    public IDbCommand CreateCommand() => throw Unscripted(nameof(CreateCommand));
    public DataTable GetSchema(string dataSourceInformation) => throw Unscripted(nameof(GetSchema));
    public DataTable GetSchema() => throw Unscripted(nameof(GetSchema));
}

/// <summary>A <see cref="ScriptedTrackedConnection"/> that is also <see cref="IAsyncDisposable"/>, so disposal code takes its async path.</summary>
internal sealed class ScriptedAsyncTrackedConnection : ScriptedTrackedConnection, IAsyncDisposable
{
    ValueTask IAsyncDisposable.DisposeAsync() => DisposeAsync();
}
