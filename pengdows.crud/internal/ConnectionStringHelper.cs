// =============================================================================
// FILE: ConnectionStringHelper.cs
// PURPOSE: Utilities for parsing and creating DbConnectionStringBuilder instances.
//
// AI SUMMARY:
// - Helper for creating DbConnectionStringBuilder with graceful fallbacks.
// - Create(factory, connectionString): Uses factory's builder if available.
// - Create(builder, connectionString): Applies connection string to builder.
// - Handles provider-specific builders that may reject certain string formats.
// - Fallback strategy:
//   1. Try provider's strongly-typed builder
//   2. If that fails, fall back to generic DbConnectionStringBuilder
//   3. For unparseable strings (like ":memory:"), stores as Data Source
// - TryApply(): Safely sets ConnectionString property, catches exceptions.
// - TrySetRawDataSource(): Sets raw value as Data Source for file paths.
// - Used during DatabaseContext initialization for connection string parsing.
// =============================================================================

using System.Data.Common;

namespace pengdows.crud.@internal;

internal static class ConnectionStringHelper
{
    internal const string DataSourceKey = "Data Source";

    public static DbConnectionStringBuilder Create(DbProviderFactory factory, string? connectionString)
    {
        var preferred = factory?.CreateConnectionStringBuilder();
        return Create(preferred, connectionString);
    }

    public static DbConnectionStringBuilder Create(DbConnectionStringBuilder? builder, string? connectionString)
    {
        var input = connectionString ?? string.Empty;

        if (builder != null)
        {
            if (TryApply(builder, input))
            {
                return builder;
            }

            var fallback = new DbConnectionStringBuilder();

            if (!string.IsNullOrEmpty(input) && TrySetRawDataSource(fallback, input))
            {
                return fallback;
            }

            TryApply(fallback, input);
            return fallback;
        }

        var parsed = new DbConnectionStringBuilder();

        if (!TryApply(parsed, input) && !string.IsNullOrEmpty(input))
        {
            TrySetRawDataSource(parsed, input);
        }

        return parsed;
    }

    private static bool TryApply(DbConnectionStringBuilder? builder, string connectionString)
    {
        if (builder is null)
        {
            return false;
        }

        try
        {
            builder.ConnectionString = connectionString;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetRawDataSource(DbConnectionStringBuilder builder, string value)
    {
        try
        {
            builder[DataSourceKey] = value;
            return true;
        }
        catch
        {
            return false;
        }
    }
}