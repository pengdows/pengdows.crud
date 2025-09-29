using System;
using System.Data;
using System.Data.Common;
using pengdows.crud.enums;

namespace pengdows.crud.types.coercion;

/// <summary>
/// Factory for creating provider-optimized database parameters.
/// Handles provider-specific parameter configuration for optimal performance.
/// </summary>
public static class ProviderParameterFactory
{
    /// <summary>
    /// Configure a parameter with provider-specific optimizations for a given value and type.
    /// </summary>
    public static bool TryConfigureParameter(
        DbParameter parameter,
        Type valueType,
        object? value,
        SupportedDatabase provider,
        CoercionRegistry? coercionRegistry = null)
    {
        coercionRegistry ??= CoercionRegistry.Shared;

        // First try provider-specific coercion
        if (coercionRegistry.TryWrite(value, parameter, provider))
        {
            ApplyProviderSpecificOptimizations(parameter, valueType, provider);
            return true;
        }

        // Fall back to general coercion
        if (coercionRegistry.TryWrite(value, parameter))
        {
            ApplyProviderSpecificOptimizations(parameter, valueType, provider);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Apply provider-specific parameter optimizations after basic configuration.
    /// </summary>
    private static void ApplyProviderSpecificOptimizations(
        DbParameter parameter,
        Type valueType,
        SupportedDatabase provider)
    {
        switch (provider)
        {
            case SupportedDatabase.PostgreSql:
                ApplyPostgreSqlOptimizations(parameter, valueType);
                break;
            case SupportedDatabase.SqlServer:
                ApplySqlServerOptimizations(parameter, valueType);
                break;
            case SupportedDatabase.MySql:
            case SupportedDatabase.MariaDb:
                ApplyMySqlOptimizations(parameter, valueType);
                break;
            case SupportedDatabase.Oracle:
                ApplyOracleOptimizations(parameter, valueType);
                break;
            case SupportedDatabase.Sqlite:
                ApplySqliteOptimizations(parameter, valueType);
                break;
            case SupportedDatabase.DuckDB:
                ApplyDuckDbOptimizations(parameter, valueType);
                break;
        }
    }

    private static void ApplyPostgreSqlOptimizations(DbParameter parameter, Type valueType)
    {
        var paramTypeName = parameter.GetType().Name;
        if (!paramTypeName.StartsWith("Npgsql")) return;

        try
        {
            var npgsqlDbTypeProperty = parameter.GetType().GetProperty("NpgsqlDbType");
            if (npgsqlDbTypeProperty == null) return;

            // Optimize common types for PostgreSQL
            if (valueType == typeof(Guid) || valueType == typeof(Guid?))
            {
                // NpgsqlDbType.Uuid = 27
                npgsqlDbTypeProperty.SetValue(parameter, 27);
            }
            else if (valueType == typeof(string[]))
            {
                // NpgsqlDbType.Array | NpgsqlDbType.Text = (1 << 30) | 16
                npgsqlDbTypeProperty.SetValue(parameter, (1 << 30) | 16);
            }
            else if (valueType == typeof(int[]))
            {
                // NpgsqlDbType.Array | NpgsqlDbType.Integer = (1 << 30) | 1
                npgsqlDbTypeProperty.SetValue(parameter, (1 << 30) | 1);
            }
            else if (valueType.Name.Contains("JsonValue") || valueType.Name.Contains("JsonElement"))
            {
                // NpgsqlDbType.Jsonb = 14 (prefer JSONB for performance)
                npgsqlDbTypeProperty.SetValue(parameter, 14);
            }
            else if (valueType.Name.Contains("HStore"))
            {
                // NpgsqlDbType.Hstore = 37
                npgsqlDbTypeProperty.SetValue(parameter, 37);
            }
            else if (valueType.Name.Contains("Range"))
            {
                // Determine range type based on generic parameter
                if (valueType.IsGenericType)
                {
                    var genericArg = valueType.GetGenericArguments()[0];
                    if (genericArg == typeof(int))
                    {
                        // NpgsqlDbType.IntegerRange = 33
                        npgsqlDbTypeProperty.SetValue(parameter, 33);
                    }
                    else if (genericArg == typeof(decimal))
                    {
                        // NpgsqlDbType.NumericRange = 34
                        npgsqlDbTypeProperty.SetValue(parameter, 34);
                    }
                    else if (genericArg == typeof(DateTime))
                    {
                        // NpgsqlDbType.TimestampRange = 35
                        npgsqlDbTypeProperty.SetValue(parameter, 35);
                    }
                }
            }
        }
        catch
        {
            // Fall back to standard DbType if provider-specific setup fails
        }
    }

    private static void ApplySqlServerOptimizations(DbParameter parameter, Type valueType)
    {
        // SQL Server specific optimizations
        if (valueType == typeof(Guid) || valueType == typeof(Guid?))
        {
            parameter.DbType = DbType.Guid;
        }
        else if (valueType.Name.Contains("JsonValue") || valueType.Name.Contains("JsonElement"))
        {
            parameter.DbType = DbType.String;
            parameter.Size = -1; // NVARCHAR(MAX) for JSON
        }
        else if (valueType == typeof(byte[]) && parameter.Size == 8)
        {
            // Optimize for rowversion/timestamp
            parameter.DbType = DbType.Binary;
            parameter.Size = 8;
        }
        else if (valueType == typeof(decimal) && parameter.DbType == DbType.Currency)
        {
            // SQL Server money type optimization
            parameter.DbType = DbType.Currency;
        }
    }

    private static void ApplyMySqlOptimizations(DbParameter parameter, Type valueType)
    {
        // MySQL specific optimizations
        if (valueType == typeof(bool) || valueType == typeof(bool?))
        {
            // Use TINYINT(1) for better compatibility
            parameter.DbType = DbType.Byte;
        }
        else if (valueType.Name.Contains("JsonValue") || valueType.Name.Contains("JsonElement"))
        {
            // MySQL 5.7+ native JSON type
            parameter.DbType = DbType.String;
        }
        else if (valueType == typeof(DateTime) || valueType == typeof(DateTime?))
        {
            // Handle MySQL's zero date behavior
            parameter.DbType = DbType.DateTime;
        }
    }

    private static void ApplyOracleOptimizations(DbParameter parameter, Type valueType)
    {
        // Oracle specific optimizations
        if (valueType == typeof(decimal) || valueType == typeof(decimal?))
        {
            // Oracle NUMBER precision handling
            parameter.DbType = DbType.Decimal;
            parameter.Precision = 38; // Oracle's maximum precision
            parameter.Scale = 10; // Default scale
        }
        else if (valueType == typeof(DateTime) || valueType == typeof(DateTime?))
        {
            // Oracle DATE vs TIMESTAMP handling
            parameter.DbType = DbType.DateTime;
        }
        else if (valueType == typeof(Guid) || valueType == typeof(Guid?))
        {
            // Oracle stores GUIDs as RAW(16)
            parameter.DbType = DbType.Binary;
            parameter.Size = 16;
        }
    }

    private static void ApplySqliteOptimizations(DbParameter parameter, Type valueType)
    {
        // SQLite has flexible typing, minimal optimizations needed
        if (valueType.Name.Contains("JsonValue") || valueType.Name.Contains("JsonElement"))
        {
            parameter.DbType = DbType.String;
        }
        else if (valueType == typeof(Guid) || valueType == typeof(Guid?))
        {
            // SQLite can store GUID as TEXT or BLOB
            parameter.DbType = DbType.String; // Use TEXT for better readability
        }
    }

    private static void ApplyDuckDbOptimizations(DbParameter parameter, Type valueType)
    {
        // DuckDB specific optimizations
        if (valueType == typeof(Guid) || valueType == typeof(Guid?))
        {
            // DuckDB has native UUID support
            parameter.DbType = DbType.Guid;
        }
        else if (valueType.IsArray)
        {
            // DuckDB LIST type support
            parameter.DbType = DbType.Object;
        }
        else if (valueType.Name.Contains("JsonValue") || valueType.Name.Contains("JsonElement"))
        {
            // DuckDB JSON type
            parameter.DbType = DbType.String;
        }
    }
}

/// <summary>
/// Enhanced parameter binding rules that incorporate the coercion system.
/// </summary>
public static class ParameterBindingRules
{
    /// <summary>
    /// Apply cross-database parameter binding rules with coercion support.
    /// </summary>
    public static bool ApplyBindingRules(
        DbParameter parameter,
        Type valueType,
        object? value,
        SupportedDatabase provider)
    {
        // Rule 1: Never embed date/time literals - always parameterize
        if (IsDateTimeType(valueType))
        {
            EnsureDateTimeParameterization(parameter, valueType, value, provider);
            return true;
        }

        // Rule 2: Boolean normalization across providers
        if (valueType == typeof(bool) || valueType == typeof(bool?))
        {
            ApplyBooleanNormalization(parameter, value, provider);
            return true;
        }

        // Rule 3: Enum handling per provider preferences
        if (valueType.IsEnum || (valueType.IsGenericType &&
            valueType.GetGenericTypeDefinition() == typeof(Nullable<>) &&
            Nullable.GetUnderlyingType(valueType)!.IsEnum))
        {
            ApplyEnumBinding(parameter, value, provider);
            return true;
        }

        // Rule 4: Array/List handling for supported providers
        if (valueType.IsArray)
        {
            ApplyArrayBinding(parameter, valueType, value, provider);
            return true;
        }

        // Rule 5: Large object handling (JSON/XML/Binary)
        if (IsLargeObjectType(valueType))
        {
            ApplyLargeObjectBinding(parameter, valueType, value, provider);
            return true;
        }

        return false;
    }

    private static bool IsDateTimeType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType == typeof(DateTime) ||
               underlyingType == typeof(DateTimeOffset) ||
               underlyingType == typeof(TimeSpan);
    }

    private static void EnsureDateTimeParameterization(
        DbParameter parameter,
        Type valueType,
        object? value,
        SupportedDatabase provider)
    {
        parameter.Value = value ?? DBNull.Value;

        var underlyingType = Nullable.GetUnderlyingType(valueType) ?? valueType;

        if (underlyingType == typeof(DateTime))
        {
            parameter.DbType = DbType.DateTime;

            // Set DateTimeKind expectations based on provider
            if (value is DateTime dt && provider == SupportedDatabase.PostgreSql)
            {
                // PostgreSQL prefers UTC for timestamp with time zone
                if (dt.Kind == DateTimeKind.Unspecified)
                {
                    // Log warning about unspecified DateTimeKind
                }
            }
        }
        else if (underlyingType == typeof(DateTimeOffset))
        {
            parameter.DbType = DbType.DateTimeOffset;
        }
        else if (underlyingType == typeof(TimeSpan))
        {
            parameter.DbType = DbType.Time;
        }
    }

    private static void ApplyBooleanNormalization(
        DbParameter parameter,
        object? value,
        SupportedDatabase provider)
    {
        if (value == null)
        {
            parameter.Value = DBNull.Value;
            parameter.DbType = DbType.Boolean;
            return;
        }

        var boolValue = (bool)value;

        switch (provider)
        {
            case SupportedDatabase.MySql:
            case SupportedDatabase.MariaDb:
                // MySQL uses TINYINT(1) or BIT(1)
                parameter.Value = boolValue ? (byte)1 : (byte)0;
                parameter.DbType = DbType.Byte;
                break;

            default:
                parameter.Value = boolValue;
                parameter.DbType = DbType.Boolean;
                break;
        }
    }

    private static void ApplyEnumBinding(
        DbParameter parameter,
        object? value,
        SupportedDatabase provider)
    {
        if (value == null)
        {
            parameter.Value = DBNull.Value;
            parameter.DbType = DbType.String;
            return;
        }

        var enumValue = (Enum)value;

        switch (provider)
        {
            case SupportedDatabase.PostgreSql:
                // PostgreSQL enums stored as text by default
                parameter.Value = enumValue.ToString();
                parameter.DbType = DbType.String;
                break;

            case SupportedDatabase.SqlServer:
            case SupportedDatabase.MySql:
            case SupportedDatabase.MariaDb:
            case SupportedDatabase.Oracle:
                // Most providers prefer integer storage for enums
                parameter.Value = Convert.ToInt32(enumValue);
                parameter.DbType = DbType.Int32;
                break;

            default:
                parameter.Value = enumValue.ToString();
                parameter.DbType = DbType.String;
                break;
        }
    }

    private static void ApplyArrayBinding(
        DbParameter parameter,
        Type valueType,
        object? value,
        SupportedDatabase provider)
    {
        if (value == null)
        {
            parameter.Value = DBNull.Value;
            parameter.DbType = DbType.Object;
            return;
        }

        switch (provider)
        {
            case SupportedDatabase.PostgreSql:
                // Native array support
                parameter.Value = value;
                parameter.DbType = DbType.Object;
                break;

            case SupportedDatabase.DuckDB:
                // Native LIST support
                parameter.Value = value;
                parameter.DbType = DbType.Object;
                break;

            default:
                // For other providers, consider temp table strategy or JSON serialization
                // For now, fall back to JSON representation
                parameter.Value = System.Text.Json.JsonSerializer.Serialize(value);
                parameter.DbType = DbType.String;
                break;
        }
    }

    private static bool IsLargeObjectType(Type type)
    {
        return type.Name.Contains("JsonValue") ||
               type.Name.Contains("JsonElement") ||
               type.Name.Contains("JsonDocument") ||
               type == typeof(string) || // Could be XML or large text
               type == typeof(byte[]); // Could be large binary
    }

    private static void ApplyLargeObjectBinding(
        DbParameter parameter,
        Type valueType,
        object? value,
        SupportedDatabase provider)
    {
        if (value == null)
        {
            parameter.Value = DBNull.Value;
            return;
        }

        // Stream large objects to provider to avoid LOH allocations
        if (valueType == typeof(byte[]) && value is byte[] bytes && bytes.Length > 85000)
        {
            // Use streaming for large binary data
            parameter.Value = new System.IO.MemoryStream(bytes);
            parameter.DbType = DbType.Binary;
        }
        else if (valueType == typeof(string) && value is string text && text.Length > 8000)
        {
            // Set appropriate size for large text
            parameter.Value = value;
            parameter.DbType = DbType.String;
            parameter.Size = -1; // Use MAX size
        }
        else
        {
            parameter.Value = value;

            if (valueType.Name.Contains("Json"))
            {
                parameter.DbType = DbType.String;
            }
            else if (valueType == typeof(byte[]))
            {
                parameter.DbType = DbType.Binary;
            }
            else
            {
                parameter.DbType = DbType.String;
            }
        }
    }
}