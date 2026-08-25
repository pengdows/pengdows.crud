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
//   SqlDialect.GetIsolationProfileMapping / SqlDialect.GetIsolationGuarantees), one override per
//   dialect file — NOT a switch here. This class only orchestrates: resolve a profile (optionally
//   substituting a stronger/weaker level per IsolationResolutionPolicy when the dialect's own ideal
//   isn't directly available), detect degradation relative to the profile's guarantee, and validate
//   a level. Levels are ranked as a partial order over dialect-declared IsolationGuarantees flags,
//   never by comparing raw IsolationLevel enum values — a "stronger-sounding" level whose
//   guarantees aren't a superset of what was requested (e.g. a blocking Serializable vs. a
//   non-blocking Snapshot) is treated as incomparable, not as an acceptable substitute.
// - Resolve(profile, policy): Returns IsolationLevel.
// - ResolveWithDetail(profile, policy): Returns IsolationResolution with degradation/kind info.
// - Validate(level): Throws if level not supported by database.
// - GetSupportedLevels(): Returns set of supported levels for current database.
// - Constructor params: dialect, readCommittedSnapshotEnabled, allowSnapshotIsolation.
// =============================================================================

using System.Data;
using System.Numerics;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.isolation;

internal sealed class IsolationResolver : IIsolationResolver
{
    private readonly SqlDialect _dialect;
    private readonly SupportedDatabase _product;
    private readonly Dictionary<IsolationProfile, IsolationLevel> _profileMap;
    private readonly bool _rcsi;
    private readonly HashSet<IsolationLevel> _supportedLevels;

    /// <summary>
    /// Which isolation levels a database supports, what each portable <see cref="IsolationProfile"/>
    /// maps to, and what guarantees each level actually provides, is dialect-owned data
    /// (<see cref="SqlDialect.GetSupportedIsolationLevels"/>/<see cref="SqlDialect.GetIsolationProfileMapping"/>/
    /// <see cref="SqlDialect.GetIsolationGuarantees"/>) — not a separate per-database switch
    /// duplicated here. This resolver is a thin orchestrator over that data plus the
    /// profile-resolution/degradation/validation logic that's genuinely product-agnostic.
    /// </summary>
    internal IsolationResolver(
        SqlDialect dialect,
        bool readCommittedSnapshotEnabled,
        bool allowSnapshotIsolation)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        _dialect = dialect;
        _product = dialect.DatabaseType;
        _rcsi = readCommittedSnapshotEnabled;
        _supportedLevels = dialect.GetSupportedIsolationLevels(allowSnapshotIsolation);
        _profileMap = dialect.GetIsolationProfileMapping(allowSnapshotIsolation);
    }

    public IsolationLevel Resolve(IsolationProfile profile) =>
        Resolve(profile, IsolationResolutionPolicy.AllowHigher);

    public IsolationLevel Resolve(IsolationProfile profile, IsolationResolutionPolicy policy) =>
        ResolveWithDetail(profile, policy).Level;

    public IsolationResolution ResolveWithDetail(IsolationProfile profile) =>
        ResolveWithDetail(profile, IsolationResolutionPolicy.AllowHigher);

    public IsolationResolution ResolveWithDetail(IsolationProfile profile, IsolationResolutionPolicy policy)
    {
        if (!_profileMap.TryGetValue(profile, out var idealLevel))
        {
            throw new NotSupportedException($"Profile {profile} not supported for {_product}");
        }

        IsolationLevel resolvedLevel;
        if (_supportedLevels.Contains(idealLevel))
        {
            resolvedLevel = idealLevel;
        }
        else
        {
            var substitute = FindSubstitute(idealLevel, policy);
            if (substitute is null)
            {
                throw new NotSupportedException(
                    $"No isolation level satisfying profile {profile} (dialect ideal {idealLevel}) is available " +
                    $"for {_product} under resolution policy {policy}.");
            }

            resolvedLevel = substitute.Value;
        }

        var (kind, degraded) = ClassifyAgainstProfileGuarantee(profile, resolvedLevel);

        Validate(resolvedLevel);
        return new IsolationResolution(profile, resolvedLevel, degraded, kind);
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

    /// <summary>
    /// Finds the nearest supported substitute for <paramref name="requested"/> permitted by
    /// <paramref name="policy"/>, preferring a strictly-stronger level (exact already handled by
    /// the caller) over a strictly-weaker one when both are allowed.
    /// </summary>
    private IsolationLevel? FindSubstitute(IsolationLevel requested, IsolationResolutionPolicy policy)
    {
        if (policy.HasFlag(IsolationResolutionPolicy.AllowHigher))
        {
            var higher = NearestByRelationship(requested, IsolationLevelComparison.Higher);
            if (higher is not null)
            {
                return higher;
            }
        }

        if (policy.HasFlag(IsolationResolutionPolicy.AllowLower))
        {
            var lower = NearestByRelationship(requested, IsolationLevelComparison.Lower);
            if (lower is not null)
            {
                return lower;
            }
        }

        return null;
    }

    private IsolationLevel? NearestByRelationship(IsolationLevel requested, IsolationLevelComparison direction)
    {
        var requestedGuarantees = _dialect.GetIsolationGuarantees(requested);

        IsolationLevel? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in _supportedLevels)
        {
            var candidateGuarantees = _dialect.GetIsolationGuarantees(candidate);
            if (Compare(requestedGuarantees, candidateGuarantees) != direction)
            {
                continue;
            }

            var distance = BitOperations.PopCount((uint)(requestedGuarantees ^ candidateGuarantees));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Classifies <paramref name="resolvedLevel"/> against the profile's fixed, database-independent
    /// canonical guarantee (see <see cref="CanonicalIdealLevel"/>) using this dialect's own
    /// <see cref="SqlDialect.GetIsolationGuarantees"/> mapping. This is what replaces the old
    /// per-product/per-profile hardcoded degradation checks: the comparison is always the same
    /// generic guarantee-superset test, and every database-specific fact it depends on
    /// (what a level actually guarantees here) comes from the dialect.
    /// </summary>
    private (IsolationResolutionKind Kind, bool Degraded) ClassifyAgainstProfileGuarantee(
        IsolationProfile profile,
        IsolationLevel resolvedLevel)
    {
        var idealGuarantees = _dialect.GetIsolationGuarantees(CanonicalIdealLevel(profile));
        var resolvedGuarantees = _dialect.GetIsolationGuarantees(resolvedLevel);

        return Compare(idealGuarantees, resolvedGuarantees) switch
        {
            IsolationLevelComparison.Exact => (IsolationResolutionKind.Exact, false),
            IsolationLevelComparison.Higher => (IsolationResolutionKind.Higher, false),
            _ => (IsolationResolutionKind.Lower, true)
        };
    }

    /// <summary>
    /// The isolation level that best represents each profile's name in the abstract, independent
    /// of any specific database's capabilities. This is not a per-database switch — it is one
    /// fixed table used purely as the reference point for degradation reporting; which concrete
    /// level actually gets used still comes entirely from <see cref="SqlDialect.GetIsolationProfileMapping"/>.
    /// </summary>
    private static IsolationLevel CanonicalIdealLevel(IsolationProfile profile) => profile switch
    {
        IsolationProfile.SafeNonBlockingReads => IsolationLevel.Snapshot,
        IsolationProfile.StrictConsistency => IsolationLevel.Serializable,
        IsolationProfile.FastWithRisks => IsolationLevel.ReadUncommitted,
        _ => throw new NotSupportedException($"Profile {profile} has no canonical isolation guarantee.")
    };

    private static IsolationLevelComparison Compare(IsolationGuarantees requested, IsolationGuarantees candidate)
    {
        if (requested == candidate)
        {
            return IsolationLevelComparison.Exact;
        }

        var candidateIsSuperset = (candidate & requested) == requested;
        var requestedIsSuperset = (requested & candidate) == candidate;

        if (candidateIsSuperset)
        {
            return IsolationLevelComparison.Higher;
        }

        if (requestedIsSuperset)
        {
            return IsolationLevelComparison.Lower;
        }

        return IsolationLevelComparison.Incomparable;
    }
}
