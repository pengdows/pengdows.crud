#region

using System.Data;

#endregion

namespace pengdows.crud;

/// <summary>
/// Represents an active database transaction. Nested transactions are not supported;
/// calling <see cref="IDatabaseContext.BeginTransaction"/> on an existing transaction will throw.
/// </summary>
public interface ITransactionContext : IDatabaseContext
{
    /// <summary>
    /// Identifier for the current transaction.
    /// </summary>
    Guid TransactionId { get; }

    /// <summary>
    /// Indicates whether the transaction has been committed.
    /// </summary>
    bool WasCommitted { get; }

    /// <summary>
    /// Indicates whether the transaction has been rolled back.
    /// </summary>
    bool WasRolledBack { get; }

    /// <summary>
    /// True once the transaction has completed, either through commit or rollback.
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// Isolation level of the active transaction.
    /// </summary>
    IsolationLevel IsolationLevel { get; }

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    void Commit();

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    void Rollback();

    /// <summary>
    /// Creates a named savepoint within the transaction scope.
    /// </summary>
    /// <param name="name">Savepoint identifier.</param>
    Task SavepointAsync(string name);

    /// <summary>
    /// Rolls back the transaction to the specified savepoint.
    /// </summary>
    /// <param name="name">Savepoint identifier.</param>
    Task RollbackToSavepointAsync(string name);
}