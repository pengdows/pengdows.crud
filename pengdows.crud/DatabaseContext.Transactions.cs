// =============================================================================
// FILE: DatabaseContext.Transactions.cs
// PURPOSE: Transaction creation and isolation level management.
//
// AI SUMMARY:
// - BeginTransaction() overloads for starting database transactions:
//   * With IsolationLevel - Native ADO.NET isolation level
//   * With IsolationProfile - Portable isolation semantics
// - Isolation level validation and resolution:
//   * Ensures requested level is supported by the database
//   * Degrades gracefully with logging when exact level unavailable
// - Read-only transaction support for read replicas.
// - IsolationProfile mapping:
//   * SafeNonBlockingReads - Snapshot isolation where available
//   * RepeatableReads - Serializable-lite semantics
// - Returns TransactionContext which pins a connection for the duration.
// - NOT compatible with TransactionScope - uses pengdows.crud's own model.
// =============================================================================

using System.Data;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.exceptions;

namespace pengdows.crud;

/// <summary>
/// DatabaseContext partial class: Transaction creation and management.
/// </summary>
/// <remarks>
/// This partial provides transaction factory methods that create
/// <see cref="TransactionContext"/> instances with appropriate isolation
/// levels and connection handling.
/// </remarks>
public partial class DatabaseContext
{
    /// <inheritdoc/>
    public ITransactionContext BeginTransaction(
        IsolationLevel? isolationLevel = null,
        ExecutionType executionType = ExecutionType.Write)
    {
        var (ro, resolved) = ResolveTransactionParameters(isolationLevel, executionType);
        return TransactionContext.Create(this, resolved, executionType);
    }

    /// <inheritdoc/>
    public ITransactionContext BeginTransaction(
        IsolationProfile isolationProfile,
        ExecutionType executionType = ExecutionType.Write)
    {
        if (isolationProfile == IsolationProfile.SafeNonBlockingReads
            && Product == SupportedDatabase.PostgreSql)
        {
            throw new TransactionModeNotSupportedException(
                "IsolationProfile.SafeNonBlockingReads requires read-committed snapshot semantics, which PostgreSQL does not provide.");
        }

        var level = _isolationResolver.Resolve(isolationProfile);
        return BeginTransaction(level, executionType);
    }

    /// <inheritdoc/>
    public async ValueTask<ITransactionContext> BeginTransactionAsync(
        IsolationLevel? isolationLevel = null,
        ExecutionType executionType = ExecutionType.Write,
        CancellationToken cancellationToken = default)
    {
        var (ro, resolved) = ResolveTransactionParameters(isolationLevel, executionType);
        return await TransactionContext.CreateAsync(this, resolved, executionType,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<ITransactionContext> BeginTransactionAsync(
        IsolationProfile isolationProfile,
        ExecutionType executionType = ExecutionType.Write,
        CancellationToken cancellationToken = default)
    {
        if (isolationProfile == IsolationProfile.SafeNonBlockingReads
            && Product == SupportedDatabase.PostgreSql)
        {
            throw new TransactionModeNotSupportedException(
                "IsolationProfile.SafeNonBlockingReads requires read-committed snapshot semantics, which PostgreSQL does not provide.");
        }

        var level = _isolationResolver.Resolve(isolationProfile);
        return await BeginTransactionAsync(level, executionType, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves transaction parameters (read-only flag, isolation level) from the input arguments.
    /// Shared by both sync and async transaction creation paths.
    /// </summary>
    private (bool readOnly, IsolationLevel isolationLevel) ResolveTransactionParameters(
        IsolationLevel? isolationLevel,
        ExecutionType executionType)
    {
        var ro = executionType == ExecutionType.Read;
        if (ro)
        {
            if (!_isReadConnection)
            {
                throw new InvalidOperationException("Context is not readable.");
            }

            if (isolationLevel is null)
            {
                var resolution = _isolationResolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);
                isolationLevel = resolution.Level;
                if (resolution.Degraded)
                {
                    _logger.LogWarning(
                        "Isolation profile {Profile} degraded to {Level} for {Product}; SnapshotIsolationEnabled={SnapshotEnabled}, RCSIEnabled={RcsiEnabled}.",
                        resolution.Profile,
                        resolution.Level,
                        Product,
                        SnapshotIsolationEnabled,
                        RCSIEnabled);
                }
            }
            else
            {
                _isolationResolver.Validate(isolationLevel.Value);
            }
        }
        else
        {
            if (!_isWriteConnection)
            {
                throw new NotSupportedException("Context is read-only.");
            }

            if (isolationLevel is null)
            {
                var supported = _isolationResolver.GetSupportedLevels();
                if (supported.Contains(IsolationLevel.ReadCommitted))
                {
                    isolationLevel = IsolationLevel.ReadCommitted;
                }
                else if (supported.Contains(IsolationLevel.Serializable))
                {
                    isolationLevel = IsolationLevel.Serializable;
                }
                else
                {
                    isolationLevel = supported.First();
                }
            }
            else
            {
                _isolationResolver.Validate(isolationLevel.Value);
            }
        }

        return (ro, isolationLevel.Value);
    }
}
