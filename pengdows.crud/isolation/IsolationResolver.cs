#region

using System.Data;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.isolation;

public sealed class IsolationResolver : IIsolationResolver
{
    private readonly SupportedDatabase _product;
    private readonly Dictionary<IsolationProfile, IsolationLevel> _profileMap;
    private readonly bool _rcsi;
    private readonly HashSet<IsolationLevel> _supportedLevels;

    internal IsolationResolver(SupportedDatabase product, bool readCommittedSnapshotEnabled)
    {
        _product = product;
        _rcsi = readCommittedSnapshotEnabled;
        _supportedLevels = BuildSupportedIsolationLevels(product, _rcsi);
        _profileMap = BuildProfileMapping(product, _rcsi);
    }

    public IsolationLevel Resolve(IsolationProfile profile)
    {
        if (!_profileMap.TryGetValue(profile, out var level))
        {
            throw new NotSupportedException($"Profile {profile} not supported for {_product}");
        }

        Validate(level);
        return level;
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

    private static HashSet<IsolationLevel> BuildSupportedIsolationLevels(SupportedDatabase db, bool rcsi)
    {
        var map = new Dictionary<SupportedDatabase, HashSet<IsolationLevel>>
        {
            [SupportedDatabase.SqlServer] = rcsi ? new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadUncommitted,
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable,
                IsolationLevel.Snapshot
            } : new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadUncommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable,
                IsolationLevel.Snapshot
            },

            [SupportedDatabase.PostgreSql] = new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadCommitted,
                        IsolationLevel.RepeatableRead,
                        IsolationLevel.Serializable,
                        IsolationLevel.Snapshot
                    },

            [SupportedDatabase.PostgreSql] = new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            },

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
            ],

            [SupportedDatabase.DuckDB] =
            [
                IsolationLevel.Serializable
            ],

            [SupportedDatabase.Unknown] =
            [
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            ]
        };

        return map.TryGetValue(db, out var set) ? set : throw new NotSupportedException($"Unsupported DB: {db}");
    }

    private static Dictionary<IsolationProfile, IsolationLevel> BuildProfileMapping(SupportedDatabase db, bool rcsi)
    {
        return db switch
        {
            SupportedDatabase.SqlServer => new()
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.Snapshot,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadUncommitted
            },

            SupportedDatabase.PostgreSql => rcsi ? new()
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadCommitted
            } : new()
            {
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadCommitted
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

            SupportedDatabase.DuckDB => new()
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.Serializable,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable
            },

            SupportedDatabase.Unknown => new()
            {
                [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
                [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
                [IsolationProfile.FastWithRisks] = IsolationLevel.ReadCommitted
            },

            _ => throw new NotSupportedException($"Isolation profile mapping not defined for DB: {db}")
        };
    }
}
