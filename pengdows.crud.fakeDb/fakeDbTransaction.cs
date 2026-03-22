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

    public override void Commit()
    {
        if (CommitException != null)
        {
            throw CommitException;
        }
    }

    public override void Rollback()
    {
        if (RollbackException != null)
        {
            throw RollbackException;
        }
    }

    public override IsolationLevel IsolationLevel { get; }
}