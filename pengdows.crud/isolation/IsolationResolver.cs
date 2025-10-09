#region

using System.Collections.Generic;
using System.Data;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.isolation;

public sealed class IsolationResolver : IIsolationResolver
{
    private readonly SupportedDatabase _product;
    private readonly Dictionary<IsolationProfile, IsolationLevel> _profileMap;
    private readonly bool _rcsi;
    private readonly bool _allowSnapshotIsolation;
    private readonly HashSet<IsolationLevel> _supportedLevels;

    internal IsolationResolver(
        SupportedDatabase product,
        bool readCommittedSnapshotEnabled,
        bool allowSnapshotIsolation)
    {
        _product = product;
        _rcsi = readCommittedSnapshotEnabled;
        _allowSnapshotIsolation = allowSnapshotIsolation;
        _supportedLevels = BuildSupportedIsolationLevels(product, _allowSnapshotIsolation);
        _profileMap = BuildProfileMapping(product, _allowSnapshotIsolation);
    }

    public IsolationLevel Resolve(IsolationProfile profile)
    {
        return ResolveWithDetail(profile).Level;
    }

    public IsolationResolution ResolveWithDetail(IsolationProfile profile)
    {
        if (!_profileMap.TryGetValue(profile, out var level))
        {
            throw new NotSupportedException($"Profile {profile} not supported for {_product}");
        }

        var degraded = false;
        if (_product == SupportedDatabase.SqlServer && profile == IsolationProfile.SafeNonBlockingReads)
        {
            if (level == IsolationLevel.Snapshot && !_supportedLevels.Contains(IsolationLevel.Snapshot))
            {
                level = IsolationLevel.ReadCommitted;
                degraded = !_rcsi;
            }
            else if (level == IsolationLevel.ReadCommitted)
            {
                degraded = !_rcsi;
            }
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

    private static HashSet<IsolationLevel> BuildSupportedIsolationLevels(
        SupportedDatabase db,
        bool allowSnapshotIsolation)
    {
        return db switch
        {
            SupportedDatabase.SqlServer => allowSnapshotIsolation
                ? new HashSet<IsolationLevel>
                {
                    IsolationLevel.ReadUncommitted,
                    IsolationLevel.ReadCommitted,
                    IsolationLevel.RepeatableRead,
                    IsolationLevel.Serializable,
                    IsolationLevel.Snapshot
                }
                : new HashSet<IsolationLevel>
                {
                    IsolationLevel.ReadUncommitted,
                    IsolationLevel.ReadCommitted,
                    IsolationLevel.RepeatableRead,
                    IsolationLevel.Serializable
                },
            SupportedDatabase.PostgreSql => new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            },
            SupportedDatabase.CockroachDb => new HashSet<IsolationLevel>
            {
                IsolationLevel.Serializable
            },
            SupportedDatabase.Sqlite => new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadCommitted,
                IsolationLevel.Serializable
            },
            SupportedDatabase.Firebird => new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadCommitted,
                IsolationLevel.Snapshot,
                IsolationLevel.Serializable
            },
            SupportedDatabase.MySql => new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadUncommitted,
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            },
            SupportedDatabase.MariaDb => new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadUncommitted,
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            },
            SupportedDatabase.Oracle => new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadCommitted,
                IsolationLevel.Serializable
            },
            SupportedDatabase.DuckDB => new HashSet<IsolationLevel>
            {
                IsolationLevel.Serializable
            },
            _ => new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            }
        };
    }

    private static Dictionary<IsolationProfile, IsolationLevel> BuildProfileMapping(
        SupportedDatabase db,
        bool allowSnapshotIsolation)
    {
        return db switch
        {
            SupportedDatabase.SqlServer => new Dictionary<IsolationProfile, IsolationLevel>
            {
                [IsolationProfile.SafeNonBlockingReads] = allowSnapshotIsolation
                    ? IsolationLevel.Snapshot
                    : IsolationLevel.ReadCommitted,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadUncommitted
            },
            SupportedDatabase.PostgreSql => new Dictionary<IsolationProfile, IsolationLevel>
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadCommitted
            },
            SupportedDatabase.CockroachDb => new Dictionary<IsolationProfile, IsolationLevel>
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.Serializable,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable
            },
            SupportedDatabase.Sqlite => new Dictionary<IsolationProfile, IsolationLevel>
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable
            },
            SupportedDatabase.Firebird => new Dictionary<IsolationProfile, IsolationLevel>
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.Snapshot,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable
            },
            SupportedDatabase.MySql => new Dictionary<IsolationProfile, IsolationLevel>
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.RepeatableRead,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadUncommitted
            },
            SupportedDatabase.MariaDb => new Dictionary<IsolationProfile, IsolationLevel>
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.RepeatableRead,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadUncommitted
            },
            SupportedDatabase.Oracle => new Dictionary<IsolationProfile, IsolationLevel>
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable
            },
            SupportedDatabase.DuckDB => new Dictionary<IsolationProfile, IsolationLevel>
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.Serializable,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable
            },
            _ => new Dictionary<IsolationProfile, IsolationLevel>
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadCommitted
            }
        };
    }
}
