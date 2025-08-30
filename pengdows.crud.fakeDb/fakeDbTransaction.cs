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

    public override void Commit()
    {
        //do nothing
    }

    public override void Rollback()
    {
        //do nothing
    }

    public override IsolationLevel IsolationLevel { get; }
}

