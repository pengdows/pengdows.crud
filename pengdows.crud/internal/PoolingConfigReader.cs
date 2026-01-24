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
                PoolingEnabled: null,
                MinPoolSize: defaultMin,
                MaxPoolSize: defaultMax,
                Source: PoolConfigSource.DialectDefault);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new PoolConfig(
                PoolingEnabled: null,
                MinPoolSize: defaultMin,
                MaxPoolSize: defaultMax,
                Source: PoolConfigSource.DialectDefault);
        }

        DbConnectionStringBuilder b;
        try
        {
            b = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch
        {
            return new PoolConfig(
                PoolingEnabled: null,
                MinPoolSize: defaultMin,
                MaxPoolSize: defaultMax,
                Source: PoolConfigSource.DialectDefault);
        }

        bool? poolingEnabled = TryGetBool(b, dialect.PoolingSettingName!);
        int? maxPool = TryGetInt(b, dialect.MaxPoolSizeSettingName!);
        int? minPool = dialect.MinPoolSizeSettingName is { Length: > 0 }
            ? TryGetInt(b, dialect.MinPoolSizeSettingName!)
            : null;

        // If pooling is explicitly disabled, treat provider pool as "no limit".
        if (poolingEnabled == false)
        {
            return new PoolConfig(
                PoolingEnabled: false,
                MinPoolSize: minPool ?? defaultMin,
                MaxPoolSize: null,
                Source: maxPool.HasValue ? PoolConfigSource.ConnectionString : PoolConfigSource.PoolingDisabled);
        }

        // Prefer explicit connection string values.
        if (maxPool.HasValue || minPool.HasValue || poolingEnabled.HasValue)
        {
            return new PoolConfig(
                PoolingEnabled: poolingEnabled,
                MinPoolSize: minPool ?? defaultMin,
                MaxPoolSize: maxPool ?? defaultMax,
                Source: PoolConfigSource.ConnectionString);
        }

        return new PoolConfig(
            PoolingEnabled: null,
            MinPoolSize: defaultMin,
            MaxPoolSize: defaultMax,
            Source: PoolConfigSource.DialectDefault);
    }

    private static bool? TryGetBool(DbConnectionStringBuilder b, string key)
        => b.TryGetValue(key, out var v) ? ParseBool(v) : null;

    private static int? TryGetInt(DbConnectionStringBuilder b, string key)
        => b.TryGetValue(key, out var v) ? ParseInt(v) : null;

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
