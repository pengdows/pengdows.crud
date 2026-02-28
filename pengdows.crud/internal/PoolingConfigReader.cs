// =============================================================================
// FILE: PoolingConfigReader.cs
// PURPOSE: Reads effective pooling configuration from dialect and connection string.
//
// AI SUMMARY:
// - Determines effective pool configuration from multiple sources.
// - PoolConfigSource enum: ConnectionString, DialectDefault, PoolingDisabled.
// - PoolConfig record: PoolingEnabled, MinPoolSize, MaxPoolSize, Source.
// - GetEffectivePoolConfig(): Combines dialect defaults with connection string settings.
// - Priority: Explicit connection string values > dialect defaults.
// - Returns null for max pool size when pooling is disabled.
// - Parses common variants: true/false, 1/0, integer values.
// - Uses dialect properties for setting names (e.g., "Max Pool Size").
// - Falls back to dialect defaults when connection string is empty/invalid.
// =============================================================================

using System.Data.Common;
using pengdows.crud.dialects;

namespace pengdows.crud.@internal;

internal enum PoolConfigSource
{
    ConnectionString,
    DialectDefault
}

internal sealed record PoolConfig(bool? PoolingEnabled, int? MinPoolSize, int? MaxPoolSize, PoolConfigSource Source);

internal static class PoolingConfigReader
{
    private static readonly string[] MaxPoolSizeAliases =
    {
        "Max Pool Size",
        "MaxPoolSize",
        "Maximum Pool Size",
        "MaximumPoolSize"
    };

    private static readonly string[] MinPoolSizeAliases =
    {
        "Min Pool Size",
        "MinPoolSize",
        "Minimum Pool Size",
        "MinimumPoolSize"
    };

    public static PoolConfig GetEffectivePoolConfig(SqlDialect dialect, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        // Dialect-level max-pool default.
        var defaultMax = dialect.DefaultMaxPoolSize;

        if (!dialect.SupportsExternalPooling ||
            string.IsNullOrWhiteSpace(dialect.PoolingSettingName) ||
            string.IsNullOrWhiteSpace(dialect.MaxPoolSizeSettingName))
        {
            return new PoolConfig(
                null,
                null,
                defaultMax,
                PoolConfigSource.DialectDefault);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new PoolConfig(
                null,
                null,
                defaultMax,
                PoolConfigSource.DialectDefault);
        }

        DbConnectionStringBuilder b;
        try
        {
            b = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch
        {
            return new PoolConfig(
                null,
                null,
                defaultMax,
                PoolConfigSource.DialectDefault);
        }

        var poolingEnabled = TryGetBool(b, dialect.PoolingSettingName!);
        var maxPool = TryGetInt(b, dialect.MaxPoolSizeSettingName!);
        var minPool = dialect.MinPoolSizeSettingName is { Length: > 0 }
            ? TryGetInt(b, dialect.MinPoolSizeSettingName!)
            : null;

        // If pooling is explicitly disabled, retain explicit max/min if provided but do not
        // fall back to dialect defaults (callers use null to mean "no pool size constraint").
        // Note: ApplyPoolingDefaults will have already thrown for Standard/KeepAlive/SingleWriter
        // modes, so this path is reached only for informational reads or SingleConnection mode.
        if (poolingEnabled == false)
        {
            return new PoolConfig(false, null, maxPool, PoolConfigSource.ConnectionString);
        }

        // Prefer explicit connection string values.
        if (maxPool.HasValue || minPool.HasValue || poolingEnabled.HasValue)
        {
            return new PoolConfig(
                poolingEnabled,
                minPool,
                maxPool ?? defaultMax,
                PoolConfigSource.ConnectionString);
        }

        return new PoolConfig(
            null,
            null,
            defaultMax,
            PoolConfigSource.DialectDefault);
    }

    private static bool? TryGetBool(DbConnectionStringBuilder b, string key)
    {
        foreach (var candidate in GetKeyCandidates(key))
        {
            if (b.TryGetValue(candidate, out var value))
            {
                return ParseBool(value);
            }
        }

        return null;
    }

    private static int? TryGetInt(DbConnectionStringBuilder b, string key)
    {
        foreach (var candidate in GetKeyCandidates(key))
        {
            if (b.TryGetValue(candidate, out var value))
            {
                return ParseInt(value);
            }
        }

        return null;
    }

    private static IEnumerable<string> GetKeyCandidates(string key)
    {
        yield return key;

        foreach (var alias in GetAdditionalAliases(key))
        {
            if (!string.Equals(alias, key, StringComparison.OrdinalIgnoreCase))
            {
                yield return alias;
            }
        }
    }

    private static IEnumerable<string> GetAdditionalAliases(string key)
    {
        if (MatchesAliasFamily(key, MaxPoolSizeAliases))
        {
            return MaxPoolSizeAliases;
        }

        if (MatchesAliasFamily(key, MinPoolSizeAliases))
        {
            return MinPoolSizeAliases;
        }

        return Array.Empty<string>();
    }

    private static bool MatchesAliasFamily(string key, IEnumerable<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (string.Equals(alias, key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool? ParseBool(object v)
    {
        if (v is bool b)
        {
            return b;
        }

        var s = v.ToString();
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        if (bool.TryParse(s, out var r))
        {
            return r;
        }

        // Common variants.
        if (s == "1")
        {
            return true;
        }

        if (s == "0")
        {
            return false;
        }

        return null;
    }

    private static int? ParseInt(object v)
    {
        if (v is int i)
        {
            return i;
        }

        var s = v.ToString();
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        return int.TryParse(s, out var r) ? r : null;
    }
}
