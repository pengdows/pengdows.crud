#region

using System.Data;

#endregion

namespace pengdows.crud;

/// <summary>
/// Represents an active database transaction. Nested transactions are not supported; calling
/// <see cref="IDatabaseContext.BeginTransaction"/> on an existing transaction will throw.
/// </summary>
public interface ITransactionContext : IDatabaseContext
{
    bool WasCommitted { get; }
    bool WasRolledBack { get; }
    bool IsCompleted { get; }
    IsolationLevel IsolationLevel { get; }
    void Commit();
    void Rollback();
    Task SavepointAsync(string name);
    Task RollbackToSavepointAsync(string name);
}