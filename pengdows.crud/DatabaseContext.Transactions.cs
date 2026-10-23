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
        ExecutionType executionType = ExecutionType.Write,
        IsolationResolutionPolicy policy = IsolationResolutionPolicy.AllowHigher)
    {
        var level = ResolveIsolationProfileForTransaction(isolationProfile, policy);
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
        CancellationToken cancellationToken = default,
        IsolationResolutionPolicy policy = IsolationResolutionPolicy.AllowHigher)
    {
        var level = ResolveIsolationProfileForTransaction(isolationProfile, policy);
        return await BeginTransactionAsync(level, executionType, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves an <see cref="IsolationProfile"/> to a concrete <see cref="IsolationLevel"/> for
    /// <see cref="BeginTransaction(IsolationProfile,ExecutionType,IsolationResolutionPolicy)"/>/
    /// <see cref="BeginTransactionAsync(IsolationProfile,ExecutionType,CancellationToken,IsolationResolutionPolicy)"/>,
    /// throwing <see cref="NotSupportedException"/> when the engine can't honor the profile's guarantee unless the
    /// caller allows a weaker level (<see cref="IsolationResolutionPolicy.AllowLower"/>), in which case the
    /// downgrade is logged as a warning.
    /// </summary>
    private IsolationLevel ResolveIsolationProfileForTransaction(IsolationProfile isolationProfile, IsolationResolutionPolicy policy)
    {
        var resolution = _isolationResolver.ResolveWithDetail(isolationProfile, policy);
        if (resolution.Degraded)
        {
            _logger.LogWarning(
                "Isolation profile {Profile} degraded to {Level} for {Product}; the requested profile's guarantee is not fully honored by this database.",
                resolution.Profile,
                resolution.Level,
                Product);
        }

        return resolution.Level;
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
                // No level was asked for, so nothing was promised. Use the non-blocking-read mapping when the
                // product can honor it; otherwise the database's own default, silently.
                var resolution = _isolationResolver.ResolveWithDetail(
                    IsolationProfile.SafeNonBlockingReads, IsolationResolutionPolicy.AllowLower);
                isolationLevel = resolution.Degraded ? DefaultIsolationLevel() : resolution.Level;
            }
            else
            {
                // An explicit level is a minimum: use it, or the weakest stronger supported level.
                isolationLevel = _isolationResolver.ResolveAtLeast(isolationLevel.Value);
            }
        }
        else
        {
            if (!_isWriteConnection)
            {
                // ReadOnlyContextException already extends NotSupportedException (no behavior
                // change for a caller catching that broader type) and additionally implements
                // IReadOnlyViolation, matching every other read-only rejection path — a plain
                // NotSupportedException here broke the documented "catch (IReadOnlyViolation)
                // catches any of the three uniformly" contract for this one path (the earliest
                // of all of them: rejected before any connection is even touched).
                throw new ReadOnlyContextException("Context is read-only.");
            }

            if (isolationLevel is null)
            {
                isolationLevel = DefaultIsolationLevel();
            }
            else
            {
                // An explicit level is a minimum: use it, or the weakest stronger supported level.
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
