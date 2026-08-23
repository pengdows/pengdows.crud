#region

using System.Data;
using System.Data.Common;

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

    public override void Commit()
    {
        CommitCallCount++;
        if (CommitException != null)
        {
            throw CommitException;
        }
    }

    public override void Rollback()
    {
        RollbackCallCount++;
        if (RollbackException != null)
        {
            throw RollbackException;
        }
    }

    public override IsolationLevel IsolationLevel { get; }
}