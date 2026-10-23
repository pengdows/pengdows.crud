#region

using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace pengdows.crud.fakeDb;

public class fakeDbTransaction : DbTransaction, IDbTransaction
{
    private readonly fakeDbConnection _connection;
    private bool _completed;
    private bool _disposed;

    public fakeDbTransaction(fakeDbConnection fakeDbConnection, IsolationLevel level)
    {
        _connection = fakeDbConnection;
        IsolationLevel = level;
    }

    // A committed, rolled back or disposed transaction is no longer associated with a connection, and it cannot complete
    // again (DbTransaction.Connection, Commit and Rollback), unless the connection's driver quirks say otherwise.
    protected override DbConnection? DbConnection
    {
        get
        {
            var quirks = _connection.DriverQuirks;
            if (_disposed)
            {
                if (quirks.DisposedTransactionThrowsObjectDisposed)
                {
                    throw new ObjectDisposedException(nameof(fakeDbTransaction));
                }

                return quirks.DisposedTransactionKeepsConnection ? _connection : null;
            }

            return _completed && !quirks.CompletedTransactionKeepsConnection ? null : _connection;
        }
    }

    /// <summary>The connection that began this transaction, whether or not it has completed.</summary>
    internal fakeDbConnection OwningConnection => _connection;

    private void ThrowIfCompleted()
    {
        if ((_completed || _disposed) && !_connection.DriverQuirks.TransactionCompletesAgain)
        {
            throw new InvalidOperationException("This transaction has completed; it is no longer usable.");
        }
    }

    private readonly OperationLog _log = new();

    /// <summary>
    /// The provider-contract members called on this transaction, in order: Commit, CommitAsync, Rollback, RollbackAsync,
    /// Dispose and DisposeAsync, and Save(name), Rollback(name) and Release(name) (their async forms record as these). A
    /// sync member and its async twin are different entries.
    /// </summary>
    public IReadOnlyList<string> Operations => _log.Snapshot();

    /// <summary>When set, Commit() throws this exception.</summary>
    public Exception? CommitException { get; set; }

    /// <summary>When set, Rollback() throws this exception.</summary>
    public Exception? RollbackException { get; set; }

    /// <summary>When set, Dispose() throws this exception (after counting the call).</summary>
    public Exception? DisposeException { get; set; }

    /// <summary>Number of times <see cref="Commit"/> was invoked, whether or not it then threw.</summary>
    public int CommitCallCount { get; private set; }

    /// <summary>Number of times <see cref="Rollback"/> was invoked, whether or not it then threw.</summary>
    public int RollbackCallCount { get; private set; }

    /// <summary>Number of times <see cref="CommitAsync"/> (not the sync <see cref="Commit"/>) was invoked.</summary>
    public int CommitAsyncCallCount { get; private set; }

    /// <summary>Number of times <see cref="RollbackAsync"/> (not the sync <see cref="Rollback"/>) was invoked.</summary>
    public int RollbackAsyncCallCount { get; private set; }

    /// <summary>Number of times Dispose() (sync or async) actually disposed this transaction.</summary>
    public int DisposeCallCount { get; private set; }

    /// <summary>
    /// When set, Commit() blocks on this gate after incrementing <see cref="CommitCallCount"/> and
    /// signaling <see cref="CommitStarted"/>, but before returning — lets a test pause a commit
    /// mid-flight to race a concurrent Dispose against it.
    /// </summary>
    public ManualResetEventSlim? CommitGate { get; set; }

    /// <summary>Same as <see cref="CommitGate"/>, for Rollback().</summary>
    public ManualResetEventSlim? RollbackGate { get; set; }

    /// <summary>Signaled the instant Commit() begins executing, before it waits on <see cref="CommitGate"/> — lets a test know it is now safe to race a concurrent Dispose.</summary>
    public ManualResetEventSlim? CommitStarted { get; set; }

    /// <summary>Same as <see cref="CommitStarted"/>, for Rollback().</summary>
    public ManualResetEventSlim? RollbackStarted { get; set; }

    public override void Commit()
    {
        _log.Record("Commit");
        CommitCallCount++;
        CommitStarted?.Set();
        CommitGate?.Wait();
        if (CommitException != null)
        {
            throw CommitException;
        }

        ThrowIfCompleted();
        _completed = true;
    }

    public override void Rollback()
    {
        _log.Record("Rollback");
        RollbackCallCount++;
        RollbackStarted?.Set();
        RollbackGate?.Wait();
        if (RollbackException != null)
        {
            throw RollbackException;
        }

        ThrowIfCompleted();
        _completed = true;
    }

    public override Task CommitAsync(CancellationToken cancellationToken = default)
    {
        _log.Record("CommitAsync");
        CommitAsyncCallCount++;
        try
        {
            _log.Unlogged(Commit);
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }

        return Task.CompletedTask;
    }

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        _log.Record("RollbackAsync");
        RollbackAsyncCallCount++;
        try
        {
            _log.Unlogged(Rollback);
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }

        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _log.Record("Dispose");
            DisposeCallCount++;
            _disposed = true;
            if (DisposeException != null)
            {
                throw DisposeException;
            }
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        _log.Record("DisposeAsync");
        _log.Unlogged(Dispose);
        return ValueTask.CompletedTask;
    }

    public override IsolationLevel IsolationLevel { get; }

    // SupportsSavepoints is false, so every savepoint method throws NotSupportedException (DbTransaction.SupportsSavepoints:
    // "If false, the methods SaveAsync, RollbackAsync and ReleaseAsync as well as their synchronous counterparts are
    // expected to throw NotSupportedException"), though DbTransaction.Release itself does nothing. The async methods are
    // .NET's, which call these. The driver quirks reproduce the drivers that depart from this.
    public override void Save(string savepointName)
    {
        _log.Record($"Save({savepointName})");
        ThrowIfSavepointsUnsupported(allowedByQuirk: false);
    }

    public override void Rollback(string savepointName)
    {
        _log.Record($"Rollback({savepointName})");
        ThrowIfSavepointsUnsupported(allowedByQuirk: false);
    }

    public override void Release(string savepointName)
    {
        _log.Record($"Release({savepointName})");
        ThrowIfSavepointsUnsupported(allowedByQuirk: _connection.DriverQuirks.SavepointReleaseIsNoOp);
    }

    private void ThrowIfSavepointsUnsupported(bool allowedByQuirk)
    {
        if (!allowedByQuirk && !_connection.DriverQuirks.SavepointsWorkButUnreported)
        {
            throw new NotSupportedException("Savepoints are not supported.");
        }
    }
}