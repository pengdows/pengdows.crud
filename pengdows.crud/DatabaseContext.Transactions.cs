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
        ExecutionType executionType = ExecutionType.Write,
        bool? readOnly = null)
    {
        var ro = readOnly ?? executionType == ExecutionType.Read;
        if (ro)
        {
            executionType = ExecutionType.Read;
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

            if (executionType == ExecutionType.Read)
            {
                throw new InvalidOperationException("Write transaction requested with read execution type.");
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
        }

        return TransactionContext.Create(this, isolationLevel.Value, executionType, ro);
    }

    /// <inheritdoc/>
    public ITransactionContext BeginTransaction(
        IsolationProfile isolationProfile,
        ExecutionType executionType = ExecutionType.Write,
        bool? readOnly = null)
    {
        if (isolationProfile == IsolationProfile.SafeNonBlockingReads
            && Product == SupportedDatabase.PostgreSql)
        {
            throw new TransactionModeNotSupportedException(
                "IsolationProfile.SafeNonBlockingReads requires read-committed snapshot semantics, which PostgreSQL does not provide.");
        }

        var level = _isolationResolver.Resolve(isolationProfile);
        return BeginTransaction(level, executionType, readOnly);
    }
}
