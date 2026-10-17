using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.isolation;
using Xunit;

namespace pengdows.crud.Tests.isolation;

/// <summary>
/// One contract for "the database cannot satisfy this": asking for an <see cref="IsolationProfile"/> that cannot be met always
/// throws <see cref="TransactionModeNotSupportedException"/> (a <see cref="NotSupportedException"/>, so a handler written for
/// the earlier types still catches it); asking for an explicit native level that nothing at or above satisfies throws
/// <see cref="InvalidOperationException"/>, as documented since 2.0.0.
/// </summary>
public sealed class IsolationRefusalContractTests
{
    private sealed class ConfigurableDialect : Sql92Dialect
    {
        private readonly HashSet<IsolationLevel> _levels;
        private readonly Dictionary<IsolationProfile, IsolationLevel> _map;

        internal ConfigurableDialect(HashSet<IsolationLevel> levels, Dictionary<IsolationProfile, IsolationLevel> map)
            : base(new fakeDbFactory(SupportedDatabase.Unknown), NullLogger.Instance)
        {
            _levels = levels;
            _map = map;
        }

        internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) => _levels;

        internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) => _map;
    }

    private static IsolationResolver Resolver(HashSet<IsolationLevel> levels, Dictionary<IsolationProfile, IsolationLevel> map) =>
        new(new ConfigurableDialect(levels, map), readCommittedSnapshotEnabled: false, allowSnapshotIsolation: false);

    [Fact]
    public void AProfileTheDatabaseDoesNotMap_ThrowsTransactionModeNotSupported()
    {
        var resolver = Resolver(
            new HashSet<IsolationLevel> { IsolationLevel.ReadCommitted },
            new Dictionary<IsolationProfile, IsolationLevel> { [IsolationProfile.FastWithRisks] = IsolationLevel.ReadCommitted });

        Assert.Throws<TransactionModeNotSupportedException>(() => resolver.ResolveWithDetail(IsolationProfile.StrictConsistency));
    }

    [Fact]
    public void AProfileMappedToALevelTheDatabaseDoesNotSupport_ThrowsTransactionModeNotSupported()
    {
        var resolver = Resolver(
            new HashSet<IsolationLevel> { IsolationLevel.ReadCommitted },
            new Dictionary<IsolationProfile, IsolationLevel> { [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable });

        Assert.Throws<TransactionModeNotSupportedException>(() => resolver.ResolveWithDetail(IsolationProfile.StrictConsistency));
    }

    [Fact]
    public void AnExplicitLevelNothingAtOrAboveSatisfies_ThrowsInvalidOperation()
    {
        var resolver = Resolver(
            new HashSet<IsolationLevel> { IsolationLevel.ReadCommitted },
            new Dictionary<IsolationProfile, IsolationLevel>());

        Assert.Throws<InvalidOperationException>(() => resolver.ResolveAtLeast(IsolationLevel.Serializable));
    }

    [Fact]
    public void TheProfileRefusalIsStillANotSupportedException_ForHandlersWrittenAgainstTheEarlierType()
    {
        var resolver = Resolver(
            new HashSet<IsolationLevel> { IsolationLevel.ReadCommitted },
            new Dictionary<IsolationProfile, IsolationLevel>());

        Assert.ThrowsAny<NotSupportedException>(() => resolver.ResolveWithDetail(IsolationProfile.StrictConsistency));
    }
}
