#region

using System.Data;
using System.Data.Common;

#endregion

namespace pengdow.crud.FakeDb;

public class FakeDbTransaction : DbTransaction, IDbTransaction
{
    public FakeDbTransaction(FakeDbConnection fakeDbConnection, IsolationLevel level)
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