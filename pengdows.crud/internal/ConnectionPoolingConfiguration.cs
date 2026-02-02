// =============================================================================
// FILE: ConnectionPoolingConfiguration.cs
// PURPOSE: Configures connection pooling defaults across database providers.
//
// AI SUMMARY:
// - Manages connection pool settings for optimal performance.
// - DefaultMinPoolSize (1): Ensures at least one pooled connection exists.
// - Key methods:
//   * IsPoolingDisabled(): Checks if Pooling=false in connection string
//   * HasMinPoolSize(): Detects if min pool size is already configured
//   * TrySetMinPoolSize(): Sets minimum pool size via property or indexer
//   * ApplyPoolingDefaults(): Adds Pooling=true and MinPoolSize if not present
//   * ApplyApplicationName(): Adds application name to connection string
// - Handles multiple provider-specific aliases (Min Pool Size, MinPoolSize, etc.).
// - Only applies to Standard and KeepAlive modes with external pooling.
// - Skips raw connection strings like ":memory:" or file paths.
// - Uses reflection for strongly-typed builder properties as fallback.
// =============================================================================

using System.Data.Common;
using System.Globalization;
using System.Reflection;
using pengdows.crud.enums;

namespace pengdows.crud.@internal;

/// <summary>
/// Service for configuring connection pooling defaults across database providers.
/// Handles min pool size configuration and pooling detection.
/// </summary>
internal static class ConnectionPoolingConfiguration
{
    /// <summary>
    /// Default minimum pool size to enforce pooling.
    /// </summary>
    public const int DefaultMinPoolSize = 1;

    private static readonly string[] MinPoolKeyCandidates =
    {
        "Min Pool Size",
        "MinPoolSize",
        "Minimum Pool Size",
        "MinimumPoolSize"
    };

    private static readonly string[] MinPoolPropertyCandidates =
    {
        "MinPoolSize",
        "MinimumPoolSize"
    };

    /// <summary>
    /// Checks if pooling is explicitly disabled in the connection string.
    /// </summary>
    public static bool IsPoolingDisabled(DbConnectionStringBuilder builder)
    {
        if (builder == null)
        {
            return false;
        }

        if (!builder.TryGetValue("Pooling", out var rawValue))
        {
            return false;
        }

        switch (rawValue)
        {
            case bool boolValue:
                return !boolValue;
            case string stringValue:
            {
                if (bool.TryParse(stringValue, out var parsedBool))
                {
                    return !parsedBool;
                }

                if (int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                {
                    return parsedInt == 0;
                }

                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the connection string already has a minimum pool size configured.
    /// </summary>
    public static bool HasMinPoolSize(DbConnectionStringBuilder builder)
    {
        if (builder == null)
        {
            return false;
        }

        foreach (var key in MinPoolKeyCandidates)
        {
            if (builder.ContainsKey(key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to set minimum pool size on the connection string builder.
    /// Returns false if pooling is disabled or min pool is already set.
    /// </summary>
    public static bool TrySetMinPoolSize(DbConnectionStringBuilder? builder, int minPoolSize)
    {
        if (builder == null)
        {
            return false;
        }

        if (IsPoolingDisabled(builder) || HasMinPoolSize(builder))
        {
            return false;
        }

        // Try via strongly-typed property first
        if (TrySetMinPoolViaProperty(builder, minPoolSize))
        {
            return true;
        }

        // Fallback to generic indexer
        if (TrySetMinPoolViaIndexer(builder, minPoolSize))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Applies default pooling settings to a connection string.
    /// Only modifies connection strings for Standard and KeepAlive modes with external pooling support.
    /// </summary>
    public static string ApplyPoolingDefaults(
        string connectionString,
        SupportedDatabase product,
        DbMode mode,
        bool supportsExternalPooling,
        string? poolingSettingName = null,
        string? minPoolSizeSettingName = null,
        DbConnectionStringBuilder? builder = null)
    {
        // Only apply to Standard and KeepAlive modes
        if (mode is not (DbMode.Standard or DbMode.KeepAlive))
        {
            return connectionString;
        }

        // Only apply to databases with external pooling
        if (!supportsExternalPooling)
        {
            return connectionString;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        // Use common defaults if not specified
        poolingSettingName ??= "Pooling";
        minPoolSizeSettingName ??= "Min Pool Size";

        try
        {
            // Use provided builder or create a new one
            if (builder == null)
            {
                builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            }

            // Skip raw connection strings (e.g., ":memory:", file paths)
            if (RepresentsRawConnectionString(builder, connectionString))
            {
                return connectionString;
            }

            var modified = false;

            // Set Pooling=true if not present
            if (!string.IsNullOrEmpty(poolingSettingName) &&
                !builder.ContainsKey(poolingSettingName))
            {
                builder[poolingSettingName] = true;
                modified = true;
            }

            // Set MinPoolSize if not present and pooling is enabled
            if (!string.IsNullOrEmpty(minPoolSizeSettingName) &&
                !HasMinPoolSize(builder))
            {
                // Only add MinPoolSize if pooling is enabled
                var poolingEnabled = string.IsNullOrEmpty(poolingSettingName) ||
                                     !builder.ContainsKey(poolingSettingName) ||
                                     (builder.ContainsKey(poolingSettingName) &&
                                      bool.TryParse(builder[poolingSettingName]?.ToString(), out var pooling) &&
                                      pooling);

                if (poolingEnabled)
                {
                    builder[minPoolSizeSettingName] = DefaultMinPoolSize;
                    modified = true;
                }
            }

            if (!modified)
            {
                return connectionString;
            }

            var result = builder.ConnectionString;

            // Check if the builder stripped sensitive values (e.g., PersistSecurityInfo=false)
            if (SensitiveValuesStripped(connectionString, result))
            {
                // Re-apply the modifications to a generic builder that preserves all values
                return ReapplyModifications(connectionString, builder);
            }

            return result;
        }
        catch
        {
            // If parsing fails, return original
            return connectionString;
        }
    }

    /// <summary>
    /// Applies application name to connection string if supported by dialect and configured.
    /// Does not override if application name is already set in the connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to modify.</param>
    /// <param name="applicationName">The application name to set.</param>
    /// <param name="applicationNameSettingName">The provider-specific setting name (e.g., "Application Name").</param>
    /// <param name="builder">Optional pre-existing connection string builder to reuse.</param>
    /// <returns>The modified connection string, or the original if no changes were made.</returns>
    public static string ApplyApplicationName(
        string connectionString,
        string? applicationName,
        string? applicationNameSettingName,
        DbConnectionStringBuilder? builder = null)
    {
        // Return unchanged if no app name configured or provider doesn't support it
        if (string.IsNullOrWhiteSpace(applicationName) ||
            string.IsNullOrWhiteSpace(applicationNameSettingName))
        {
            return connectionString;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        try
        {
            // Use provided builder or create a new one
            if (builder == null)
            {
                builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            }

            // Skip raw connection strings (e.g., ":memory:", file paths)
            if (RepresentsRawConnectionString(builder, connectionString))
            {
                return connectionString;
            }

            // Don't override if already set in connection string
            if (builder.ContainsKey(applicationNameSettingName))
            {
                return connectionString;
            }

            builder[applicationNameSettingName] = applicationName;
            var result = builder.ConnectionString;

            // Check if the builder stripped sensitive values (e.g., PersistSecurityInfo=false)
            if (SensitiveValuesStripped(connectionString, result))
            {
                // Re-apply the modifications to a generic builder that preserves all values
                return ReapplyModifications(connectionString, builder);
            }

            return result;
        }
        catch
        {
            // If parsing fails, return original
            return connectionString;
        }
    }

    /// <summary>
    /// Appends a suffix to the application name in the connection string when supported.
    /// Falls back to the provided application name when none is set in the connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to modify.</param>
    /// <param name="applicationNameSettingName">The provider-specific setting name (e.g., "Application Name").</param>
    /// <param name="suffix">Suffix to append (e.g., ":ro").</param>
    /// <param name="fallbackApplicationName">Fallback application name if none exists in the connection string.</param>
    /// <param name="builder">Optional pre-existing connection string builder to reuse.</param>
    /// <returns>The modified connection string, or the original if no changes were made.</returns>
    public static string ApplyApplicationNameSuffix(
        string connectionString,
        string? applicationNameSettingName,
        string suffix,
        string? fallbackApplicationName = null,
        DbConnectionStringBuilder? builder = null)
    {
        if (string.IsNullOrWhiteSpace(suffix) || string.IsNullOrWhiteSpace(applicationNameSettingName))
        {
            return connectionString;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        try
        {
            builder ??= new DbConnectionStringBuilder { ConnectionString = connectionString };

            if (RepresentsRawConnectionString(builder, connectionString))
            {
                return connectionString;
            }

            if (builder.TryGetValue(applicationNameSettingName, out var value))
            {
                var current = Convert.ToString(value) ?? string.Empty;
                if (!current.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    builder[applicationNameSettingName] = current + suffix;
                    var result = builder.ConnectionString;

                    // Check if the builder stripped sensitive values
                    if (SensitiveValuesStripped(connectionString, result))
                    {
                        return ReapplyModifications(connectionString, builder);
                    }

                    return result;
                }

                return connectionString;
            }

            if (!string.IsNullOrWhiteSpace(fallbackApplicationName))
            {
                builder[applicationNameSettingName] = $"{fallbackApplicationName}{suffix}";
                var result = builder.ConnectionString;

                // Check if the builder stripped sensitive values
                if (SensitiveValuesStripped(connectionString, result))
                {
                    return ReapplyModifications(connectionString, builder);
                }

                return result;
            }

            return connectionString;
        }
        catch
        {
            return connectionString;
        }
    }

    private static bool TrySetMinPoolViaProperty(DbConnectionStringBuilder builder, int minPoolSize)
    {
        foreach (var candidate in MinPoolPropertyCandidates)
        {
            var property = builder.GetType().GetProperty(candidate,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property == null || !property.CanWrite)
            {
                continue;
            }

            var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            try
            {
                var converted = Convert.ChangeType(minPoolSize, targetType, CultureInfo.InvariantCulture);
                property.SetValue(builder, converted);
                return true;
            }
            catch
            {
                // Try next candidate
            }
        }

        return false;
    }

    private static bool TrySetMinPoolViaIndexer(DbConnectionStringBuilder builder, int minPoolSize)
    {
        foreach (var key in MinPoolKeyCandidates)
        {
            try
            {
                builder[key] = minPoolSize;
                return true;
            }
            catch
            {
                // Try next alias
            }
        }

        return false;
    }

    private static bool RepresentsRawConnectionString(DbConnectionStringBuilder builder, string original)
    {
        if (builder == null)
        {
            return true;
        }

        // If the builder only contains "Data Source" and it matches the original,
        // this is likely a raw path like ":memory:" or "data.db"
        if (!builder.TryGetValue("Data Source", out var raw) || builder.Count != 1)
        {
            return false;
        }

        return string.Equals(Convert.ToString(raw), original, StringComparison.Ordinal);
    }

    private static readonly string[] SensitiveKeys =
    {
        "password", "pwd", "user id", "uid", "user", "username"
    };

    /// <summary>
    /// Checks if sensitive values (like password) were stripped when converting the connection string.
    /// Many providers have PersistSecurityInfo=false by default, stripping passwords on read.
    /// </summary>
    private static bool SensitiveValuesStripped(string original, string modified)
    {
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(modified))
        {
            return false;
        }

        if (string.Equals(original, modified, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var originalBuilder = new DbConnectionStringBuilder { ConnectionString = original };
            var modifiedBuilder = new DbConnectionStringBuilder { ConnectionString = modified };

            foreach (var sensitiveKey in SensitiveKeys)
            {
                // Check if original had this sensitive key with a value
                if (originalBuilder.TryGetValue(sensitiveKey, out var originalValue) &&
                    !string.IsNullOrWhiteSpace(originalValue?.ToString()))
                {
                    // Check if it was stripped or emptied in the modified version
                    if (!modifiedBuilder.TryGetValue(sensitiveKey, out var modifiedValue) ||
                        string.IsNullOrWhiteSpace(modifiedValue?.ToString()))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            // If we can't parse, assume potential stripping for safety
            return true;
        }
    }

    /// <summary>
    /// Re-applies modifications from a provider-specific builder to a generic builder
    /// that preserves all values including sensitive ones.
    /// </summary>
    private static string ReapplyModifications(string original, DbConnectionStringBuilder providerBuilder)
    {
        try
        {
            // Start with the original connection string (which has all values)
            var genericBuilder = new DbConnectionStringBuilder { ConnectionString = original };

            // Apply all values from the provider builder that may have been added/modified
            foreach (var keyObj in providerBuilder.Keys)
            {
                var key = keyObj?.ToString();
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                // Skip sensitive keys - we want to keep the originals
                var lowerKey = key.ToLowerInvariant();
                var isSensitive = false;
                foreach (var sensitiveKey in SensitiveKeys)
                {
                    if (lowerKey == sensitiveKey || lowerKey.Contains("password") || lowerKey.Contains("secret"))
                    {
                        isSensitive = true;
                        break;
                    }
                }

                if (isSensitive)
                {
                    continue;
                }

                // Copy non-sensitive values that may have been added (Pooling, MinPoolSize, Application Name, etc.)
                if (providerBuilder.TryGetValue(key, out var value))
                {
                    genericBuilder[key] = value;
                }
            }

            return genericBuilder.ConnectionString;
        }
        catch
        {
            // If re-application fails, return original to preserve credentials
            return original;
        }
    }
}
