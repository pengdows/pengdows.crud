using System.Data.Common;

namespace pengdows.crud.@internal;

internal static class ConnectionStringHelper
{
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
            builder["Data Source"] = value;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
