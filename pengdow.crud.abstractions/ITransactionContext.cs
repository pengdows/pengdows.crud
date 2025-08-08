#region

using System.Data;

#endregion

namespace pengdow.crud;

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
}