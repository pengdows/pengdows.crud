#region

using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace pengdows.crud.fakeDb;

public class fakeDbTransaction : DbTransaction, IDbTransaction
{
    public fakeDbTransaction(fakeDbConnection fakeDbConnection, IsolationLevel level)
    {
        DbConnection = fakeDbConnection;
        IsolationLevel = level;
    }

    protected override DbConnection? DbConnection { get; }

    private readonly OperationLog _log = new();

    /// <summary>
    /// The provider-contract members called on this transaction, in order: Commit, CommitAsync, Rollback, RollbackAsync,
    /// Dispose and DisposeAsync. A sync member and its async twin are different entries.
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
    }

    public override Task CommitAsync(CancellationToken cancellationToken = default)
    {
        _log.Record("CommitAsync");
        CommitAsyncCallCount++;
        _log.Unlogged(Commit);
        return Task.CompletedTask;
    }

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        _log.Record("RollbackAsync");
        RollbackAsyncCallCount++;
        _log.Unlogged(Rollback);
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _log.Record("Dispose");
            DisposeCallCount++;
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
}