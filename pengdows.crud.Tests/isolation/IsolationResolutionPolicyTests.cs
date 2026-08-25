#region

using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.isolation;
using Xunit;

#endregion

namespace pengdows.crud.Tests.isolation;

/// <summary>
/// Exercises IsolationResolver's substitution/escalation machinery directly via
/// <see cref="ConfigurableIsolationDialect"/>. No shipped dialect's own profile mapping ever
/// targets a level outside its own supported set (each dialect's GetIsolationProfileMapping
/// already pre-selects something it declares support for), so these branches of
/// IsolationResolver — the actual policy-driven substitution search — are otherwise untested by
/// any real-dialect test. All guarantee comparisons here use SqlDialect's base ANSI defaults
/// unless a test overrides them.
/// </summary>
public class IsolationResolutionPolicyTests
{
    private static IsolationResolver CreateResolver(
        HashSet<IsolationLevel> supported,
        IsolationProfile profile,
        IsolationLevel idealLevel)
    {
        var mapping = new Dictionary<IsolationProfile, IsolationLevel> { [profile] = idealLevel };
        var dialect = new ConfigurableIsolationDialect(supported, mapping);
        return new IsolationResolver(dialect, false, false);
    }

    [Fact]
    public void ResolveWithDetail_ExactAvailable_ReturnsExact()
    {
        var resolver = CreateResolver(
            new HashSet<IsolationLevel> { IsolationLevel.ReadCommitted },
            IsolationProfile.SafeNonBlockingReads,
            IsolationLevel.ReadCommitted);

        var resolution = resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);

        Assert.Equal(IsolationLevel.ReadCommitted, resolution.Level);
    }

    [Fact]
    public void ResolveWithDetail_ExactAbsent_HigherAvailable_DefaultPolicy_ReturnsHigher()
    {
        // Ideal (ReadCommitted) isn't supported; RepeatableRead is a strict superset of its
        // guarantees, so the default AllowHigher policy should substitute it without throwing.
        var resolver = CreateResolver(
            new HashSet<IsolationLevel> { IsolationLevel.RepeatableRead },
            IsolationProfile.SafeNonBlockingReads,
            IsolationLevel.ReadCommitted);

        var resolution = resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);

        Assert.Equal(IsolationLevel.RepeatableRead, resolution.Level);
    }

    [Fact]
    public void ResolveWithDetail_ExactAbsent_OnlyLowerAvailable_DefaultPolicy_Throws()
    {
        // Ideal (RepeatableRead) isn't supported; only a strictly weaker ReadCommitted is
        // available. Isolation must never be silently weakened by default.
        var resolver = CreateResolver(
            new HashSet<IsolationLevel> { IsolationLevel.ReadCommitted },
            IsolationProfile.SafeNonBlockingReads,
            IsolationLevel.RepeatableRead);

        Assert.Throws<NotSupportedException>(() =>
            resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads));
    }

    [Fact]
    public void ResolveWithDetail_ExactAbsent_OnlyLowerAvailable_AllowLower_ReturnsLowerAndDegraded()
    {
        var resolver = CreateResolver(
            new HashSet<IsolationLevel> { IsolationLevel.ReadCommitted },
            IsolationProfile.SafeNonBlockingReads,
            IsolationLevel.RepeatableRead);

        var resolution = resolver.ResolveWithDetail(
            IsolationProfile.SafeNonBlockingReads,
            IsolationResolutionPolicy.AllowLower);

        Assert.Equal(IsolationLevel.ReadCommitted, resolution.Level);
        Assert.True(resolution.Degraded);
        Assert.Equal(IsolationResolutionKind.Lower, resolution.Kind);
    }

    [Fact]
    public void ResolveWithDetail_HigherAndLowerBothAvailable_AllowAny_PrefersHigher()
    {
        // Ideal (RepeatableRead) isn't supported. ReadCommitted is strictly weaker; Serializable's
        // guarantees are a strict superset of RepeatableRead's. AllowAny must prefer the higher
        // substitute over the lower one.
        var resolver = CreateResolver(
            new HashSet<IsolationLevel> { IsolationLevel.ReadCommitted, IsolationLevel.Serializable },
            IsolationProfile.SafeNonBlockingReads,
            IsolationLevel.RepeatableRead);

        var resolution = resolver.ResolveWithDetail(
            IsolationProfile.SafeNonBlockingReads,
            IsolationResolutionPolicy.AllowAny);

        Assert.Equal(IsolationLevel.Serializable, resolution.Level);
    }

    [Fact]
    public void ResolveWithDetail_OnlyIncomparableAlternatives_Throws()
    {
        // Snapshot (NoDirtyReads|NoNonRepeatableReads|NoPhantomReads|NonBlockingReads) and
        // Serializable (NoDirtyReads|NoNonRepeatableReads|NoPhantomReads|NoWriteSkew) are neither
        // a superset nor a subset of each other under the base ANSI guarantee mapping: Serializable
        // lacks Snapshot's non-blocking guarantee, Snapshot lacks Serializable's write-skew
        // protection. Substituting one for the other would trade away a guarantee that was
        // requested, so this must throw under AllowAny, not silently pick one.
        var resolver = CreateResolver(
            new HashSet<IsolationLevel> { IsolationLevel.Serializable },
            IsolationProfile.SafeNonBlockingReads,
            IsolationLevel.Snapshot);

        Assert.Throws<NotSupportedException>(() =>
            resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads, IsolationResolutionPolicy.AllowAny));
    }

    [Fact]
    public void ResolveWithDetail_StrictConsistency_OnlyWeakerAvailable_DefaultPolicy_Throws()
    {
        var resolver = CreateResolver(
            new HashSet<IsolationLevel> { IsolationLevel.ReadCommitted },
            IsolationProfile.StrictConsistency,
            IsolationLevel.Serializable);

        Assert.Throws<NotSupportedException>(() =>
            resolver.ResolveWithDetail(IsolationProfile.StrictConsistency));
    }

    [Fact]
    public void ResolveWithDetail_StrictConsistency_OnlyWeakerAvailable_AllowLower_ResolvesDegraded()
    {
        var resolver = CreateResolver(
            new HashSet<IsolationLevel> { IsolationLevel.ReadCommitted },
            IsolationProfile.StrictConsistency,
            IsolationLevel.Serializable);

        var resolution = resolver.ResolveWithDetail(
            IsolationProfile.StrictConsistency,
            IsolationResolutionPolicy.AllowLower);

        Assert.Equal(IsolationLevel.ReadCommitted, resolution.Level);
        Assert.True(resolution.Degraded);
        Assert.Equal(IsolationResolutionKind.Lower, resolution.Kind);
    }

    [Fact]
    public void ResolveWithDetail_MultipleHigherCandidates_PicksMinimumSufficientLevel()
    {
        // Both RepeatableRead and Serializable are strict supersets of the ideal ReadCommitted.
        // "AllowHigher" must mean the minimum level that still satisfies the requested semantics —
        // RepeatableRead — not the strongest one available.
        var resolver = CreateResolver(
            new HashSet<IsolationLevel> { IsolationLevel.RepeatableRead, IsolationLevel.Serializable },
            IsolationProfile.SafeNonBlockingReads,
            IsolationLevel.ReadCommitted);

        var resolution = resolver.ResolveWithDetail(IsolationProfile.SafeNonBlockingReads);

        Assert.Equal(IsolationLevel.RepeatableRead, resolution.Level);
    }
}
