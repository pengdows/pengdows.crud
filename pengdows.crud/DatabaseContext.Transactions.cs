using System;
using System.Data;
using System.Linq;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.isolation;

namespace pengdows.crud;

/// <summary>
/// DatabaseContext partial class: Transaction management methods
/// </summary>
public partial class DatabaseContext
{
    /// <summary>
    /// Begins a new database transaction with the specified isolation level and execution type.
    /// </summary>
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

    /// <summary>
    /// Begins a new database transaction with the specified isolation profile and execution type.
    /// </summary>
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