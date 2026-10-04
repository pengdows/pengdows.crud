// =============================================================================
// FILE: IsolationResolver.cs
// PURPOSE: Resolves IsolationProfile to database-specific IsolationLevel.
//
// AI SUMMARY:
// - Implements IIsolationResolver for portable isolation level handling.
// - Maps IsolationProfile (semantic intent) to IsolationLevel (ADO.NET).
// - Profiles:
//   * SafeNonBlockingReads: Non-blocking reads (Snapshot, RepeatableRead, ReadCommitted)
//   * StrictConsistency: Serializable where supported (TiDB/Snowflake/Access fall short
//     and are reported as Degraded)
//   * FastWithRisks: ReadUncommitted where supported
// - Database-specific mappings:
//   * SQL Server: Snapshot (if enabled), else ReadCommitted (Degraded); supports ReadUncommitted
//   * PostgreSQL/YugabyteDB: MVCC-based ReadCommitted; no ReadUncommitted
//   * MySQL/MariaDB: RepeatableRead for safe reads; has ReadUncommitted
//   * Oracle: ReadCommitted or Serializable only
//   * CockroachDB/DuckDB: Serializable only
//   * FlatFile: ReadUncommitted/ReadCommitted/RepeatableRead; no Serializable
// - Resolve(profile): Returns IsolationLevel.
// - ResolveWithDetail(profile): Returns IsolationResolution with degradation info.
// - Validate(level): Throws if level not supported by database.
// - ResolveAtLeast(level): Requested level, or the weakest stronger supported one; never weaker.
// - ResolveForTransaction(profile): Throws TransactionModeNotSupportedException when the
//   resolution is Degraded (StrictConsistency on TiDB/Snowflake/Access/FlatFile; SafeNonBlockingReads
//   on SQL Server without snapshot). PostgreSQL/YugabyteDB map SafeNonBlockingReads to
//   RepeatableRead (MVCC snapshot).
// - GetSupportedLevels(): Returns set of supported levels for current database.
// - Constructor params: dialect (owns the per-database mapping, DEC-010), readCommittedSnapshotEnabled,
//   allowSnapshotIsolation.
// =============================================================================

using System.Data;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;

namespace pengdows.crud.isolation;

internal sealed class IsolationResolver : IIsolationResolver
{
    private readonly SqlDialect _dialect;
    private readonly SupportedDatabase _product;
    private readonly Dictionary<IsolationProfile, IsolationLevel> _profileMap;
    private readonly bool _rcsi;
    private readonly HashSet<IsolationLevel> _supportedLevels;

    /// <summary>
    /// Which levels a database supports and what each <see cref="IsolationProfile"/> maps to is the
    /// dialect's data (<see cref="SqlDialect.GetSupportedIsolationLevels"/>,
    /// <see cref="SqlDialect.GetIsolationProfileMapping"/>, <see cref="SqlDialect.IsDegradedForProfile"/>);
    /// this resolver applies the product-agnostic resolution rules to it (DEC-010, as on 3.0).
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

    public IsolationLevel Resolve(IsolationProfile profile)
    {
        return ResolveWithDetail(profile).Level;
    }

    /// <summary>
    /// Resolves an isolation profile for use when beginning a transaction. Unlike the
    /// general-purpose <see cref="Resolve"/>, it rejects any Degraded resolution.
    /// </summary>
    /// <exception cref="TransactionModeNotSupportedException">The profile's guarantee cannot be met on this product.</exception>
    internal IsolationLevel ResolveForTransaction(IsolationProfile profile)
    {
        // A caller who asks for a profile gets at least that profile's guarantee, never less.
        var resolution = ResolveWithDetail(profile);
        if (resolution.Degraded)
        {
            throw new TransactionModeNotSupportedException(
                $"IsolationProfile.{profile} cannot be guaranteed on {_product}: the strongest mapping available is " +
                $"{resolution.Level}, which is weaker than the profile requires.");
        }

        return resolution.Level;
    }

    /// <summary>
    /// Resolves an explicitly requested native level to the level a transaction actually uses:
    /// the requested level when supported, otherwise the weakest supported level that is at least
    /// as strong. Never resolves to a weaker level.
    /// </summary>
    /// <exception cref="InvalidOperationException">No supported level is at least as strong as <paramref name="requested"/>.</exception>
    internal IsolationLevel ResolveAtLeast(IsolationLevel requested)
    {
        if (_supportedLevels.Contains(requested))
        {
            return requested;
        }

        foreach (var candidate in StrongerLevels(requested))
        {
            if (_supportedLevels.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Isolation level {requested} not supported by {_product} (RCSI: {_rcsi}), and no stronger level is available.");
    }

    // Levels that satisfy at least the guarantees of the requested level, weakest first.
    // Snapshot does not satisfy RepeatableRead: locking RepeatableRead prevents write skew on
    // rows it has read, which Snapshot allows.
    private static IsolationLevel[] StrongerLevels(IsolationLevel requested) => requested switch
    {
        IsolationLevel.ReadUncommitted => new[]
        {
            IsolationLevel.ReadCommitted, IsolationLevel.RepeatableRead, IsolationLevel.Snapshot,
            IsolationLevel.Serializable
        },
        IsolationLevel.ReadCommitted => new[]
            { IsolationLevel.RepeatableRead, IsolationLevel.Snapshot, IsolationLevel.Serializable },
        IsolationLevel.RepeatableRead => new[] { IsolationLevel.Serializable },
        IsolationLevel.Snapshot => new[] { IsolationLevel.Serializable },
        _ => Array.Empty<IsolationLevel>()
    };

    public IsolationResolution ResolveWithDetail(IsolationProfile profile)
    {
        if (!_profileMap.TryGetValue(profile, out var level))
        {
            throw new NotSupportedException($"Profile {profile} not supported for {_product}");
        }

        var originalLevel = level;
        var degraded = false;

        // A mapped level the dialect says falls short of the profile (SQL Server's ReadCommitted for
        // SafeNonBlockingReads when snapshot isolation is off).
        if (_dialect.IsDegradedForProfile(profile, level))
        {
            degraded = true;
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
