// =============================================================================
// FILE: ConnectionPoolingConfiguration.cs
// PURPOSE: Configures connection pooling defaults across database providers.
//
// AI SUMMARY:
// - Manages connection pool settings for optimal performance.
// - Key methods:
//   * IsPoolingDisabled(): Checks if Pooling=false in connection string
//   * HasMinPoolSize(): Detects if min pool size is already configured
//   * ApplyPoolingDefaults(): Adds Pooling=true if absent; throws if Pooling=false detected
//   * ApplyApplicationName(): Adds application name to connection string
//   * ClampMinPoolSize(): Silently corrects Min Pool Size to [0, MaxPoolSize]
// - Pooling=false is not allowed — throws InvalidOperationException.
// - Only applies to Standard, KeepAlive, and SingleWriter modes with external pooling.
// - Skips raw connection strings like ":memory:" or file paths.
// =============================================================================

using System.Data.Common;
using System.Globalization;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.@internal;

/// <summary>
/// Service for configuring connection pooling defaults across database providers.
/// Handles min pool size configuration and pooling detection.
/// </summary>
internal static class ConnectionPoolingConfiguration
{
    // Fallback connection-string key names when the dialect does not expose its own.
    private const string DefaultPoolingKey = "Pooling";

    private static readonly string[] MinPoolKeyCandidates =
    {
        "Min Pool Size",
        "MinPoolSize",
        "Minimum Pool Size",
        "MinimumPoolSize"
    };

    private static readonly string[] MaxPoolKeyCandidates =
    {
        "Max Pool Size",
        "MaxPoolSize",
        "Maximum Pool Size",
        "MaximumPoolSize"
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

        if (!builder.TryGetValue(DefaultPoolingKey, out var rawValue))
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
    /// Applies default pooling settings to a connection string.
    /// Only modifies connection strings for Standard, KeepAlive, and SingleWriter modes with external pooling support.
    /// </summary>
    public static string ApplyPoolingDefaults(
        string connectionString,
        SupportedDatabase product,
        DbMode mode,
        bool supportsExternalPooling,
        string? poolingSettingName = null,
        DbConnectionStringBuilder? builder = null)
    {
        // Only apply to modes that use provider-managed connection pooling
        if (mode is not (DbMode.Standard or DbMode.KeepAlive or DbMode.SingleWriter))
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
        poolingSettingName ??= DefaultPoolingKey;
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

            // Reject Pooling=false — pengdows.crud requires connection pooling for the
            // "open late, close early" model. Each operation checks out a connection from
            // the pool; without a pool every checkout opens a new physical connection,
            // which defeats governor slot budgets and breaks connection-count metrics.
            //
            // Migration: remove Pooling=false from your connection string. If you need
            // a single persistent connection, use DbMode.SingleConnection instead.
            if (IsPoolingDisabled(builder))
            {
                throw new InvalidOperationException(
                    $"Connection pooling must not be disabled ({poolingSettingName}=false detected). " +
                    "pengdows.crud requires connection pooling for correct operation. " +
                    "Remove the Pooling=false setting from your connection string, " +
                    "or switch to DbMode.SingleConnection if you need a single persistent connection.");
            }

            var modified = false;

            // Set Pooling=true if not present
            if (!string.IsNullOrEmpty(poolingSettingName) &&
                !builder.ContainsKey(poolingSettingName))
            {
                builder[poolingSettingName] = true;
                modified = true;
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
        catch (Exception ex) when (ex is not InvalidOperationException)
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
    /// <param name="suffix">Suffix to append (e.g., "-ro").</param>
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

    /// <summary>
    /// Injects a pool-key discriminator key/value into the connection string.
    /// Used when <c>ApplicationNameSettingName</c> is unsupported and the dialect needs
    /// an alternative attribute to differentiate reader vs writer connection pools.
    /// No-op if: either parameter is null/empty, key already present, or raw connection string.
    /// </summary>
    public static string ApplyPoolDiscriminator(
        string connectionString,
        string? discriminatorSettingName,
        string? discriminatorSettingValue,
        DbConnectionStringBuilder? builder = null)
    {
        if (string.IsNullOrWhiteSpace(discriminatorSettingName) ||
            string.IsNullOrWhiteSpace(discriminatorSettingValue) ||
            string.IsNullOrWhiteSpace(connectionString))
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

            // Don't override if user already set the discriminator key
            if (builder.ContainsKey(discriminatorSettingName))
            {
                return connectionString;
            }

            builder[discriminatorSettingName] = discriminatorSettingValue;
            var result = builder.ConnectionString;

            if (SensitiveValuesStripped(connectionString, result))
            {
                return ReapplyModifications(connectionString, builder);
            }

            return result;
        }
        catch
        {
            return connectionString;
        }
    }

    /// <summary>
    /// Sets the maximum pool size on a connection string.
    /// When <paramref name="overrideExisting"/> is false the call is a no-op if the setting
    /// is already present; when true it overwrites unconditionally (used by SingleWriter to
    /// force the writer pool to 1).
    /// </summary>
    public static string ApplyMaxPoolSize(
        string connectionString,
        int maxPoolSize,
        string? maxPoolSizeSettingName,
        bool overrideExisting = false,
        DbConnectionStringBuilder? builder = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(maxPoolSizeSettingName))
        {
            return connectionString;
        }

        if (maxPoolSize <= 0)
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

            if (!overrideExisting && builder.ContainsKey(maxPoolSizeSettingName))
            {
                return connectionString;
            }

            builder[maxPoolSizeSettingName] = maxPoolSize;
            var result = builder.ConnectionString;

            if (SensitiveValuesStripped(connectionString, result))
            {
                return ReapplyModifications(connectionString, builder);
            }

            return result;
        }
        catch
        {
            return connectionString;
        }
    }

    /// <summary>
    /// Silently corrects Min Pool Size in the connection string to a valid range.
    /// <list type="bullet">
    /// <item>Step 1: clamp to &gt;= 0 (negative values become 0)</item>
    /// <item>Step 2: clamp to &lt;= MaxPoolSize (when MaxPoolSize is known)</item>
    /// </list>
    /// Returns the original connection string when no correction is needed or when
    /// the setting name is unknown / the string cannot be parsed.
    /// </summary>
    internal static string ClampMinPoolSize(
        string connectionString,
        string? minPoolSizeSettingName,
        int? rawMin,
        int? rawMax)
    {
        if (!rawMin.HasValue || string.IsNullOrWhiteSpace(minPoolSizeSettingName) ||
            string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var clamped = Math.Max(rawMin.Value, 0);
        if (rawMax.HasValue)
        {
            clamped = Math.Min(clamped, rawMax.Value);
        }

        if (clamped == rawMin.Value)
        {
            return connectionString; // already valid — no write needed
        }

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

            if (RepresentsRawConnectionString(builder, connectionString))
            {
                return connectionString;
            }

            builder[minPoolSizeSettingName] = clamped;
            var result = builder.ConnectionString;

            if (SensitiveValuesStripped(connectionString, result))
            {
                return ReapplyModifications(connectionString, builder);
            }

            return result;
        }
        catch
        {
            return connectionString;
        }
    }

    /// <summary>
    /// Removes Max Pool Size settings when the provider does not support them.
    /// Intended for providers like SQLite (Microsoft.Data.Sqlite) and DuckDB.
    /// </summary>
    public static string StripUnsupportedMaxPoolSize(
        string connectionString,
        string? maxPoolSizeSettingName,
        DbConnectionStringBuilder? builder = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || !string.IsNullOrWhiteSpace(maxPoolSizeSettingName))
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

            var modified = false;
            foreach (var key in MaxPoolKeyCandidates)
            {
                if (builder.ContainsKey(key))
                {
                    builder.Remove(key);
                    modified = true;
                }
            }

            if (!modified)
            {
                return connectionString;
            }

            var result = builder.ConnectionString;
            if (SensitiveValuesStripped(connectionString, result))
            {
                return ReapplyModifications(connectionString, builder);
            }

            return result;
        }
        catch
        {
            return connectionString;
        }
    }

    private static bool RepresentsRawConnectionString(DbConnectionStringBuilder builder, string original)
    {
        if (builder == null)
        {
            return true;
        }

        // If the builder only contains "Data Source" and it matches the original,
        // this is likely a raw path like ":memory:" or "data.db"
        if (!builder.TryGetValue(ConnectionStringHelper.DataSourceKey, out var raw) || builder.Count != 1)
        {
            return false;
        }

        return string.Equals(Convert.ToString(raw), original, StringComparison.Ordinal);
    }

    private static readonly string[] SensitiveKeys =
    {
        "password", "pwd", "user id", "uid", "user", "username"
    };

    // Substrings that, when found anywhere in a key name (case-insensitive), mark it as sensitive.
    private static readonly string[] SensitiveKeySubstrings =
    {
        "password", "secret", "token", "access"
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
    /// Removes provider pooling settings from the connection string.
    /// </summary>
    public static string StripPoolingSetting(string connectionString, string? poolingSettingName)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

            if (RepresentsRawConnectionString(builder, connectionString))
            {
                return connectionString;
            }

            var modified = false;
            if (!string.IsNullOrWhiteSpace(poolingSettingName) && builder.ContainsKey(poolingSettingName))
            {
                builder.Remove(poolingSettingName);
                modified = true;
            }

            if (!string.Equals(poolingSettingName, DefaultPoolingKey, StringComparison.OrdinalIgnoreCase) &&
                builder.ContainsKey(DefaultPoolingKey))
            {
                builder.Remove(DefaultPoolingKey);
                modified = true;
            }

            return modified ? builder.ConnectionString : connectionString;
        }
        catch
        {
            return connectionString;
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

                // Skip sensitive keys - we want to keep the originals from the source connection string
                var isSensitive = false;
                foreach (var sensitiveKey in SensitiveKeys)
                {
                    if (string.Equals(key, sensitiveKey, StringComparison.OrdinalIgnoreCase))
                    {
                        isSensitive = true;
                        break;
                    }
                }

                if (!isSensitive)
                {
                    foreach (var substring in SensitiveKeySubstrings)
                    {
                        if (key.Contains(substring, StringComparison.OrdinalIgnoreCase))
                        {
                            isSensitive = true;
                            break;
                        }
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
