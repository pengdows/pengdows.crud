// =============================================================================
// FILE: DatabaseContext.Transactions.cs
// PURPOSE: Transaction creation and isolation level management.
//
// AI SUMMARY:
// - BeginTransaction() overloads for starting database transactions:
//   * With IsolationLevel - Native ADO.NET isolation level
//   * With IsolationProfile - Portable isolation semantics
// - Isolation level resolution "fails up", never down:
//   * An explicit unsupported IsolationLevel resolves to the weakest stronger supported
//     level (IsolationResolver.ResolveAtLeast); throws if none exists
//   * BeginTransaction(IsolationProfile) throws TransactionModeNotSupportedException
//     rather than run below the profile's guarantee (ResolveForTransaction)
//   * Transactions with no explicit level promised nothing: reads use SafeNonBlockingReads
//     when the product can honor it, otherwise (like writes) the default of ReadCommitted
//     (else Serializable, else the first supported level), silently
// - Read-only transactions (ExecutionType.Read) require a readable context.
// - Returns TransactionContext which pins a connection for the duration.
// - NOT compatible with TransactionScope - uses pengdows.crud's own model.
// =============================================================================

using System.Data;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

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
        var level = _isolationResolver.ResolveForTransaction(isolationProfile);
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
        var level = _isolationResolver.ResolveForTransaction(isolationProfile);
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
                // No level was asked for, so nothing was promised. Use the non-blocking-read mapping when
                // the product can honor it; otherwise the database's own default, silently.
                var resolution = _isolationResolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);
                isolationLevel = resolution.Degraded ? DefaultIsolationLevel() : resolution.Level;
            }
            else
            {
                isolationLevel = _isolationResolver.ResolveAtLeast(isolationLevel.Value);
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
                isolationLevel = DefaultIsolationLevel();
            }
            else
            {
                isolationLevel = _isolationResolver.ResolveAtLeast(isolationLevel.Value);
            }
        }

        return (ro, isolationLevel.Value);
    }

    /// <summary>
    /// The level used when the caller asks for none: ReadCommitted, else Serializable, else the
    /// first level the product supports.
    /// </summary>
    private IsolationLevel DefaultIsolationLevel()
    {
        var supported = _isolationResolver.GetSupportedLevels();
        if (supported.Contains(IsolationLevel.ReadCommitted))
        {
            return IsolationLevel.ReadCommitted;
        }

        return supported.Contains(IsolationLevel.Serializable) ? IsolationLevel.Serializable : supported.First();
    }
}
