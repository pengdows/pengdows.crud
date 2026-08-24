// =============================================================================
// FILE: IsolationResolver.cs
// PURPOSE: Resolves IsolationProfile to database-specific IsolationLevel.
//
// AI SUMMARY:
// - Implements IIsolationResolver for portable isolation level handling.
// - Maps IsolationProfile (semantic intent) to IsolationLevel (ADO.NET).
// - Profiles:
//   * SafeNonBlockingReads: Non-blocking reads (Snapshot, RepeatableRead, ReadCommitted)
//   * StrictConsistency: Serializable everywhere
//   * FastWithRisks: ReadUncommitted where supported
// - Database-specific support/mapping data lives on the dialect (SqlDialect.GetSupportedIsolationLevels /
//   SqlDialect.GetIsolationProfileMapping), one override per dialect file — NOT a switch here.
//   This class only orchestrates: resolve a profile, detect degradation, validate a level.
// - Resolve(profile): Returns IsolationLevel.
// - ResolveWithDetail(profile): Returns IsolationResolution with degradation info.
// - Validate(level): Throws if level not supported by database.
// - GetSupportedLevels(): Returns set of supported levels for current database.
// - Constructor params: dialect, readCommittedSnapshotEnabled, allowSnapshotIsolation.
// =============================================================================

using System.Data;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;

namespace pengdows.crud.isolation;

internal sealed class IsolationResolver : IIsolationResolver
{
    private readonly SupportedDatabase _product;
    private readonly Dictionary<IsolationProfile, IsolationLevel> _profileMap;
    private readonly bool _rcsi;
    private readonly HashSet<IsolationLevel> _supportedLevels;

    /// <summary>
    /// Which isolation levels a database supports, and what each portable
    /// <see cref="IsolationProfile"/> maps to, is dialect-owned data (<see cref="SqlDialect.GetSupportedIsolationLevels"/>/
    /// <see cref="SqlDialect.GetIsolationProfileMapping"/>) — not a separate per-database switch
    /// duplicated here. This resolver is a thin orchestrator over that data plus the
    /// profile-resolution/degradation/validation logic that's genuinely product-agnostic.
    /// </summary>
    internal IsolationResolver(
        SqlDialect dialect,
        bool readCommittedSnapshotEnabled,
        bool allowSnapshotIsolation)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        _product = dialect.DatabaseType;
        _rcsi = readCommittedSnapshotEnabled;
        _supportedLevels = dialect.GetSupportedIsolationLevels(allowSnapshotIsolation);
        _profileMap = dialect.GetIsolationProfileMapping(allowSnapshotIsolation);
    }

    public IsolationLevel Resolve(IsolationProfile profile)
    {
        return ResolveWithDetail(profile).Level;
    }

    /// <summary>
    /// Resolves an isolation profile for use when beginning a transaction, applying any
    /// product-specific rejections that don't belong in the general-purpose <see cref="Resolve"/>.
    /// </summary>
    internal IsolationLevel ResolveForTransaction(IsolationProfile profile)
    {
        return ResolveForTransactionWithDetail(profile).Level;
    }

    /// <summary>
    /// Same as <see cref="ResolveForTransaction"/> but returns the full <see cref="IsolationResolution"/>
    /// (including <see cref="IsolationResolution.Degraded"/>) so callers can surface a silent
    /// profile downgrade — e.g. <see cref="IsolationProfile.StrictConsistency"/> resolving to
    /// something weaker than Serializable on an engine that can't honor it — instead of discarding
    /// that information the way a bare <see cref="IsolationLevel"/> return would.
    /// </summary>
    internal IsolationResolution ResolveForTransactionWithDetail(IsolationProfile profile)
    {
        if (profile == IsolationProfile.SafeNonBlockingReads
            && _product is SupportedDatabase.PostgreSql or SupportedDatabase.YugabyteDb)
        {
            throw new TransactionModeNotSupportedException(
                "IsolationProfile.SafeNonBlockingReads requires read-committed snapshot semantics, which PostgreSQL does not provide.");
        }

        return ResolveWithDetail(profile);
    }

    public IsolationResolution ResolveWithDetail(IsolationProfile profile)
    {
        if (!_profileMap.TryGetValue(profile, out var level))
        {
            throw new NotSupportedException($"Profile {profile} not supported for {_product}");
        }

        var originalLevel = level;
        var degraded = false;

        if (_product == SupportedDatabase.SqlServer && profile == IsolationProfile.SafeNonBlockingReads)
        {
            // Ideal is Snapshot; if we have to use ReadCommitted, it's degraded
            if (level == IsolationLevel.Snapshot && !_supportedLevels.Contains(IsolationLevel.Snapshot))
            {
                level = IsolationLevel.ReadCommitted;
                degraded = true;
            }
            else if (level == IsolationLevel.ReadCommitted)
            {
                // Using ReadCommitted instead of Snapshot is always degraded for SafeNonBlockingReads
                degraded = true;
            }
        }

        if (level != originalLevel)
        {
            degraded = true;
        }

        // StrictConsistency's entire purpose is a Serializable-equivalent guarantee.
        // Any product whose mapping falls short of Serializable has degraded the
        // requested guarantee, regardless of which product's mapping table produced it.
        if (profile == IsolationProfile.StrictConsistency && level != IsolationLevel.Serializable)
        {
            degraded = true;
        }

        Validate(level);
        return new IsolationResolution(profile, level, degraded);
    }

    public void Validate(IsolationLevel level)
    {
        if (!_supportedLevels.Contains(level))
        {
            throw new InvalidOperationException($"Isolation level {level} not supported by {_product} (RCSI: {_rcsi})");
        }
    }

    public IReadOnlySet<IsolationLevel> GetSupportedLevels()
    {
        return _supportedLevels;
    }
}