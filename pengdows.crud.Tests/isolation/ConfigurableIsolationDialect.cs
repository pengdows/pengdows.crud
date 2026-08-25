using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.isolation;

namespace pengdows.crud.Tests.isolation;

/// <summary>
/// A dialect double with fully caller-controlled supported levels, profile mapping, and
/// guarantees. Real dialects always pre-select an already-supported level for every profile
/// (see <see cref="IsolationTestDialectFactory"/> for exercising them), so the escalation-search
/// branch of <see cref="IsolationResolver"/> (dialect's ideal not directly supported) is never hit
/// by any shipped dialect today. This double exists to exercise that branch directly against the
/// required <see cref="IsolationResolutionPolicy"/> regression cases.
/// </summary>
internal sealed class ConfigurableIsolationDialect : SqlDialect
{
    private readonly HashSet<IsolationLevel> _supported;
    private readonly Dictionary<IsolationProfile, IsolationLevel> _mapping;
    private readonly Dictionary<IsolationLevel, IsolationGuarantees> _guarantees;

    public ConfigurableIsolationDialect(
        HashSet<IsolationLevel> supported,
        Dictionary<IsolationProfile, IsolationLevel> mapping,
        Dictionary<IsolationLevel, IsolationGuarantees>? guarantees = null)
        : base(new fakeDbFactory(SupportedDatabase.Unknown), NullLogger.Instance)
    {
        _supported = supported;
        _mapping = mapping;
        _guarantees = guarantees ?? new Dictionary<IsolationLevel, IsolationGuarantees>();
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;

    internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) => _supported;

    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) => _mapping;

    internal override IsolationGuarantees GetIsolationGuarantees(IsolationLevel level) =>
        _guarantees.TryGetValue(level, out var guarantee) ? guarantee : base.GetIsolationGuarantees(level);
}
