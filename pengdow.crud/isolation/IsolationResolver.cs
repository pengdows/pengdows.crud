#region

using System.Data;
using pengdow.crud.enums;

#endregion

namespace pengdow.crud.isolation;

public sealed class IsolationResolver : IIsolationResolver
{
    private readonly SupportedDatabase _product;
    private readonly Dictionary<IsolationProfile, IsolationLevel> _profileMap;
    private readonly bool _rcsi;
    private readonly HashSet<IsolationLevel> _supportedLevels;

    public IsolationResolver(SupportedDatabase product, bool readCommittedSnapshotEnabled)
    {
        _product = product;
        _rcsi = readCommittedSnapshotEnabled;
        _supportedLevels = BuildSupportedIsolationLevels(product, _rcsi);
        _profileMap = BuildProfileMapping(product, _rcsi);
    }

    public IsolationLevel Resolve(IsolationProfile profile)
    {
        if (!_profileMap.TryGetValue(profile, out var level))
            throw new NotSupportedException($"Profile {profile} not supported for {_product}");

        if (!_rcsi && _product == SupportedDatabase.PostgreSql &&
            profile == IsolationProfile.SafeNonBlockingReads && level == IsolationLevel.ReadCommitted)
            throw new InvalidOperationException(
                $"Tenant {_product} does not have RCSI enabled. Profile {profile} maps to blocking isolation level.");

        Validate(level);
        return level;
    }

    public void Validate(IsolationLevel level)
    {
        if (!_supportedLevels.Contains(level))
            throw new InvalidOperationException($"Isolation level {level} not supported by {_product} (RCSI: {_rcsi})");
    }

    public IReadOnlySet<IsolationLevel> GetSupportedLevels()
    {
        return _supportedLevels;
    }

    private static HashSet<IsolationLevel> BuildSupportedIsolationLevels(SupportedDatabase db, bool rcsi)
    {
        var map = new Dictionary<SupportedDatabase, HashSet<IsolationLevel>>
        {
            [SupportedDatabase.SqlServer] = new HashSet<IsolationLevel>()
            {
                IsolationLevel.ReadUncommitted,
                rcsi ? IsolationLevel.ReadCommitted : default,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable,
                IsolationLevel.Snapshot
            }.Where(l => l != default).ToHashSet(),

            [SupportedDatabase.PostgreSql] =
            [
                IsolationLevel.ReadUncommitted,
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            ],

            [SupportedDatabase.CockroachDb] = [IsolationLevel.Serializable],

            [SupportedDatabase.Sqlite] =
            [
                IsolationLevel.ReadCommitted,
                IsolationLevel.Serializable
            ],
            [SupportedDatabase.Firebird] =
            [
                IsolationLevel.ReadCommitted,
                IsolationLevel.Snapshot,
                IsolationLevel.Serializable
            ],

            [SupportedDatabase.MySql] =
            [
                IsolationLevel.ReadUncommitted,
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            ],

            [SupportedDatabase.MariaDb] =
            [
                IsolationLevel.ReadUncommitted,
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            ],

            [SupportedDatabase.Oracle] =
            [
                IsolationLevel.ReadCommitted,
                IsolationLevel.Serializable
            ]

            // Add more as needed
        };

        return map.TryGetValue(db, out var set) ? set : throw new NotSupportedException($"Unsupported DB: {db}");
    }

    private static Dictionary<IsolationProfile, IsolationLevel> BuildProfileMapping(SupportedDatabase db, bool rcsi)
    {
        return db switch
        {
            SupportedDatabase.SqlServer => new()
            {
                [IsolationProfile.SafeNonBlockingReads] = rcsi
                    ? IsolationLevel.ReadCommitted
                    : IsolationLevel.Snapshot,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadUncommitted
            },

            SupportedDatabase.PostgreSql => new()
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadUncommitted
            },

            SupportedDatabase.CockroachDb => new()
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.Serializable,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable
            },
            SupportedDatabase.Sqlite => new()
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable
            },
            SupportedDatabase.Firebird => new()
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.Snapshot,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable
            },

            SupportedDatabase.MySql => new()
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadUncommitted
            },

            SupportedDatabase.MariaDb => new()
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadUncommitted
            },

            SupportedDatabase.Oracle => new()
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable
            },
            _ => throw new NotSupportedException($"Isolation profile mapping not defined for DB: {db}")
        };
    }
}