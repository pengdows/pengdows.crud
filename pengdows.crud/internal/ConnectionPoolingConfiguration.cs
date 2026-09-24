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
// - Only applies to Standard, PreventDatabaseUnload, and SingleWriter modes with external pooling.
// - Skips raw connection strings like ":memory:" or file paths.
// =============================================================================

using System.Collections.Generic;
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
    /// Only modifies connection strings for Standard, PreventDatabaseUnload, and SingleWriter modes with external pooling support.
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
        if (mode is not (DbMode.Standard or DbMode.PreventDatabaseUnload or DbMode.SingleWriter))
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

            // Set Pooling=true if not present
            if (string.IsNullOrEmpty(poolingSettingName) ||
                builder.ContainsKey(poolingSettingName))
            {
                return connectionString;
            }

            builder[poolingSettingName] = true;
            return SetSingleKey(connectionString, poolingSettingName, true);
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
            return SetSingleKey(connectionString, applicationNameSettingName, applicationName);
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
                    var newValue = current + suffix;
                    builder[applicationNameSettingName] = newValue;
                    return SetSingleKey(connectionString, applicationNameSettingName, newValue);
                }

                return connectionString;
            }

            if (!string.IsNullOrWhiteSpace(fallbackApplicationName))
            {
                var newValue = $"{fallbackApplicationName}{suffix}";
                builder[applicationNameSettingName] = newValue;
                return SetSingleKey(connectionString, applicationNameSettingName, newValue);
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
            return SetSingleKey(connectionString, discriminatorSettingName, discriminatorSettingValue);
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
            return SetSingleKey(connectionString, maxPoolSizeSettingName, maxPoolSize);
        }
        catch
        {
            return connectionString;
        }
    }

    /// <summary>
    /// Corrects Min Pool Size in the connection string to a valid range.
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

            return SetSingleKey(connectionString, minPoolSizeSettingName, clamped);
        }
        catch
        {
            return connectionString;
        }
    }

    internal static string EnsureMinimumPoolSize(
        string connectionString,
        string? minPoolSizeSettingName,
        int? rawMin,
        int? rawMax,
        int requiredMinimum)
    {
        if (string.IsNullOrWhiteSpace(minPoolSizeSettingName) ||
            string.IsNullOrWhiteSpace(connectionString) ||
            requiredMinimum < 0)
        {
            return connectionString;
        }

        if (requiredMinimum == 0 && !rawMin.HasValue)
        {
            // No enforced minimum and the caller didn't set one — nothing to add or preserve.
            return connectionString;
        }

        var target = Math.Max(rawMin ?? 0, requiredMinimum);
        if (rawMax.HasValue)
        {
            target = Math.Min(target, rawMax.Value);
        }

        if (rawMin.HasValue && target == rawMin.Value)
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

            return SetSingleKey(connectionString, minPoolSizeSettingName, target);
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

            var keysToRemove = new List<string>();
            foreach (var key in MaxPoolKeyCandidates)
            {
                if (builder.ContainsKey(key))
                {
                    keysToRemove.Add(key);
                }
            }

            if (keysToRemove.Count == 0)
            {
                return connectionString;
            }

            foreach (var key in keysToRemove)
            {
                builder.Remove(key);
            }

            return RemoveKeys(connectionString, keysToRemove);
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

            var keysToRemove = new List<string>();
            if (!string.IsNullOrWhiteSpace(poolingSettingName) && builder.ContainsKey(poolingSettingName))
            {
                keysToRemove.Add(poolingSettingName);
            }

            if (!string.Equals(poolingSettingName, DefaultPoolingKey, StringComparison.OrdinalIgnoreCase) &&
                builder.ContainsKey(DefaultPoolingKey))
            {
                keysToRemove.Add(DefaultPoolingKey);
            }

            return keysToRemove.Count == 0 ? connectionString : RemoveKeys(connectionString, keysToRemove);
        }
        catch
        {
            return connectionString;
        }
    }

    /// <summary>
    /// Applies a single key/value change to a connection string using a plain, provider-agnostic
    /// <see cref="DbConnectionStringBuilder"/> seeded from the original text.
    /// </summary>
    /// <remarks>
    /// Deliberately never uses a caller-supplied provider-typed builder's own <c>ConnectionString</c>
    /// getter to produce output — some typed builders (e.g. IBM.Data.Db2's DB2ConnectionStringBuilder)
    /// unconditionally re-serialize their entire known property schema, including dozens of
    /// unrelated, empty-valued keys nobody set. Feeding that exhaustive string back to the same
    /// driver's connection constructor can be rejected outright. A plain, untyped builder only ever
    /// echoes back the keys it was actually given, so the result always matches "the original string,
    /// plus this one change" — never more.
    /// </remarks>
    private static string SetSingleKey(string originalConnectionString, string key, object value)
    {
        var generic = new DbConnectionStringBuilder { ConnectionString = originalConnectionString };
        generic[key] = value;
        return generic.ConnectionString;
    }

    /// <summary>
    /// Removes one or more keys from a connection string using a plain, provider-agnostic
    /// <see cref="DbConnectionStringBuilder"/> seeded from the original text. See
    /// <see cref="SetSingleKey"/> for why a caller-supplied typed builder is never used here.
    /// </summary>
    private static string RemoveKeys(string originalConnectionString, List<string> keys)
    {
        var generic = new DbConnectionStringBuilder { ConnectionString = originalConnectionString };
        foreach (var key in keys)
        {
            generic.Remove(key);
        }

        return generic.ConnectionString;
    }
}
