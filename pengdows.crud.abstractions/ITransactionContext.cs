#region

using System.Data;

#endregion

namespace pengdows.crud;

public interface ITransactionContext : IDatabaseContext
{
    bool WasCommitted { get; }
    bool WasRolledBack { get; }
    bool IsCompleted { get; }
    IsolationLevel IsolationLevel { get; }
    void Commit();
    void Rollback();
}