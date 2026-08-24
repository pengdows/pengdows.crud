#region

using System.Data;
using System.Data.Common;
using System.Threading;

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

    /// <summary>When set, Commit() throws this exception.</summary>
    public Exception? CommitException { get; set; }

    /// <summary>When set, Rollback() throws this exception.</summary>
    public Exception? RollbackException { get; set; }

    /// <summary>Number of times <see cref="Commit"/> was invoked, whether or not it then threw.</summary>
    public int CommitCallCount { get; private set; }

    /// <summary>Number of times <see cref="Rollback"/> was invoked, whether or not it then threw.</summary>
    public int RollbackCallCount { get; private set; }

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
        RollbackCallCount++;
        RollbackStarted?.Set();
        RollbackGate?.Wait();
        if (RollbackException != null)
        {
            throw RollbackException;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCallCount++;
        }

        base.Dispose(disposing);
    }

    public override IsolationLevel IsolationLevel { get; }
}