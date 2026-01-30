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
    DialectDefault,
    PoolingDisabled
}

internal sealed record PoolConfig(bool? PoolingEnabled, int? MinPoolSize, int? MaxPoolSize, PoolConfigSource Source);

internal static class PoolingConfigReader
{
    public static PoolConfig GetEffectivePoolConfig(SqlDialect dialect, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        // Dialect-level suggestion defaults.
        var defaultMax = dialect.DefaultMaxPoolSize;
        var defaultMin = dialect.DefaultMinPoolSize;

        if (!dialect.SupportsExternalPooling ||
            string.IsNullOrWhiteSpace(dialect.PoolingSettingName) ||
            string.IsNullOrWhiteSpace(dialect.MaxPoolSizeSettingName))
        {
            return new PoolConfig(
                null,
                defaultMin,
                defaultMax,
                PoolConfigSource.DialectDefault);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new PoolConfig(
                null,
                defaultMin,
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
                defaultMin,
                defaultMax,
                PoolConfigSource.DialectDefault);
        }

        var poolingEnabled = TryGetBool(b, dialect.PoolingSettingName!);
        var maxPool = TryGetInt(b, dialect.MaxPoolSizeSettingName!);
        var minPool = dialect.MinPoolSizeSettingName is { Length: > 0 }
            ? TryGetInt(b, dialect.MinPoolSizeSettingName!)
            : null;

        // If pooling is explicitly disabled, treat provider pool as "no limit".
        if (poolingEnabled == false)
        {
            return new PoolConfig(
                false,
                minPool ?? defaultMin,
                null,
                maxPool.HasValue ? PoolConfigSource.ConnectionString : PoolConfigSource.PoolingDisabled);
        }

        // Prefer explicit connection string values.
        if (maxPool.HasValue || minPool.HasValue || poolingEnabled.HasValue)
        {
            return new PoolConfig(
                poolingEnabled,
                minPool ?? defaultMin,
                maxPool ?? defaultMax,
                PoolConfigSource.ConnectionString);
        }

        return new PoolConfig(
            null,
            defaultMin,
            defaultMax,
            PoolConfigSource.DialectDefault);
    }

    private static bool? TryGetBool(DbConnectionStringBuilder b, string key)
    {
        return b.TryGetValue(key, out var v) ? ParseBool(v) : null;
    }

    private static int? TryGetInt(DbConnectionStringBuilder b, string key)
    {
        return b.TryGetValue(key, out var v) ? ParseInt(v) : null;
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