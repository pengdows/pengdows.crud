// =============================================================================
// FILE: AdvancedTypeRegistry.cs
// PURPOSE: Registry for advanced database type mappings across providers.
//
// AI SUMMARY:
// - Central registry for complex/exotic database type handling.
// - Maps CLR types to provider-specific type configurations (JSON, spatial, arrays, etc.).
// - AdvancedTypeRegistry.Shared provides singleton with default mappings.
// - MappingKey: High-performance struct key (Type + SupportedDatabase) to avoid allocation.
// - CachedParameterConfig: Caches mapping + converter lookups for hot paths with version stamp.
// - RegisterMapping<T>(): Associates CLR type with ProviderTypeMapping for a database.
// - RegisterConverter<T>(): Registers AdvancedTypeConverter for complex transformations.
// - TryConfigureParameter(): Configures DbParameter with provider-specific type info.
// - TryConfigureParameterEnhanced(): Tries legacy system, then CoercionRegistry, then ParameterBindingRules.
// - Default mappings: JSON (JSONB, JSON), spatial (Geometry, Geography), arrays, ranges,
//   network types (inet, cidr, macaddr), temporal (interval), LOBs, identity/concurrency.
// - ProviderTypeMapping: Holds DbType + ConfigureParameter action for provider customization.
// - Uses cached reflection to set provider-specific enum properties (NpgsqlDbType, OracleDbType, etc.).
// - Thread-safe: All mutable collections are ConcurrentDictionary. Converter version stamp
//   avoids per-call dictionary lookup on the hot path.
// =============================================================================

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using pengdows.crud.types.coercion;

namespace pengdows.crud.types;

// Retained as internal compatibility types for existing in-assembly tests and
// diagnostics. Runtime mapping state is now owned by CoercionRegistry.
internal readonly struct MappingKey : IEquatable<MappingKey>
{
    public readonly Type ClrType;
    public readonly SupportedDatabase Provider;

    public MappingKey(Type clrType, SupportedDatabase provider)
    {
        ClrType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        Provider = provider;
    }

    public bool Equals(MappingKey other) => ClrType == other.ClrType && Provider == other.Provider;
    public override bool Equals(object? obj) => obj is MappingKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ClrType, Provider);
}

internal readonly struct CachedParameterConfig
{
    public readonly ProviderTypeMapping Mapping;
    public readonly IAdvancedTypeConverter? Converter;
    public readonly int ConverterVersion;

    public CachedParameterConfig(ProviderTypeMapping mapping, IAdvancedTypeConverter? converter, int converterVersion)
    {
        Mapping = mapping;
        Converter = converter;
        ConverterVersion = converterVersion;
    }
}

/// <summary>
/// Registry for advanced database type mappings across different providers.
/// Handles spatial, JSON, arrays, ranges, network types, etc.
/// Thread-safe: all mutable state uses ConcurrentDictionary.
/// </summary>
internal class AdvancedTypeRegistry
{
    // Provider-specific reflection property names.  Typos here fail silently at
    // runtime; centralising makes them grep-able and keeps them in sync.
    private static class NpgsqlNames
    {
        public const string DbTypeProperty = "NpgsqlDbType";
        public const string DataTypeName = "DataTypeName";
        public const string Jsonb = "Jsonb";
        public const string Integer = "Integer";
        public const string Text = "Text";
        public const string Array = "Array";
        public const string Int4Range = "Int4Range";
        public const string Int8Range = "BigIntRange";
        public const string TsRange = "TsRange";
        public const string Inet = "Inet";
        public const string Cidr = "Cidr";
        public const string MacAddr = "MacAddr8";
        public const string Interval = "Interval";
        public const string Uuid = "Uuid";
    }

    private static class OracleNames
    {
        public const string DbTypeProperty = "OracleDbType";
        public const string IntervalYM = "IntervalYM";
        public const string IntervalDS = "IntervalDS";
        public const string TimeStampTZ = "TimeStampTZ";
        public const string Blob = "Blob";
        public const string Clob = "Clob";
    }

    private static class SqlServerNames
    {
        public const string DbTypeProperty = "SqlDbType";
        public const string Udt = "Udt";
        public const string UdtTypeName = "UdtTypeName";
        public const string Timestamp = "Timestamp";
    }

    private static class MySqlNames
    {
        public const string DbTypeProperty = "MySqlDbType";
        public const string Json = "JSON";
    }

    public static AdvancedTypeRegistry Shared { get; } = new(true);

    private readonly CoercionRegistry _registry;

    // Static reflection caches — reflection results are universal across instances
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();
    private static readonly ConcurrentDictionary<(Type, string), object?> EnumCache = new();

    public AdvancedTypeRegistry(bool includeDefaults = false)
    {
        _registry = includeDefaults ? CoercionRegistry.Shared : new CoercionRegistry();
        if (includeDefaults)
        {
            RegisterDefaultMappings();
            RegisterDefaultConverters();
        }
    }

    /// <summary>
    /// Register a provider-specific type mapping for a CLR type.
    /// </summary>
    public void RegisterMapping<T>(SupportedDatabase provider, ProviderTypeMapping mapping)
    {
        var type = typeof(T);
        type = Nullable.GetUnderlyingType(type) ?? type;

        _registry.RegisterMapping<T>(provider, mapping);
    }

    /// <summary>
    /// Register a converter for complex type transformations.
    /// </summary>
    public void RegisterConverter<T>(AdvancedTypeConverter<T> converter)
    {
        _registry.RegisterConverter(converter);
    }

    /// <summary>
    /// Get provider-specific type mapping for a CLR type.
    /// </summary>
    public ProviderTypeMapping? GetMapping(Type clrType, SupportedDatabase provider)
    {
        return _registry.GetMapping(clrType, provider);
    }

    /// <summary>
    /// Get type converter for a CLR type.
    /// </summary>
    public IAdvancedTypeConverter? GetConverter(Type clrType)
    {
        return _registry.GetConverter(clrType);
    }

    /// <summary>
    /// Configure a DbParameter with provider-specific type information.
    /// High-performance version with caching and version-stamped converter tracking.
    /// </summary>
    public bool TryConfigureParameter(DbParameter parameter, Type clrType, object? value, SupportedDatabase provider)
    {
        return _registry.TryConfigureLegacyParameter(parameter, clrType, value, provider);
    }

    internal bool IsMappedType(Type clrType)
    {
        clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        return _registry.IsLegacyMappedType(clrType);
    }

    /// <summary>
    /// Configures a parameter through the single production write pipeline.
    /// Legacy provider mappings retain precedence. Types that are known to the
    /// legacy registry but have no mapping for this provider deliberately fall
    /// back to the dialect, preserving provider-specific primitive behavior.
    /// </summary>
    internal bool TryConfigureParameterForDialect(
        DbParameter parameter,
        Type clrType,
        object? value,
        SupportedDatabase provider)
    {
        if (TryConfigureParameter(parameter, clrType, value, provider))
        {
            return true;
        }

        // A legacy mapping is provider-specific. If this provider has no
        // mapping, continue into the unified coercion/binding pipeline rather
        // than allowing a mapping registered for another provider to block it.
        return ProviderParameterFactory.TryConfigureParameter(parameter, clrType, value, provider) ||
               ParameterBindingRules.ApplyBindingRules(parameter, clrType, value, provider);
    }

    /// <summary>
    /// Enhanced parameter configuration using both legacy converters and new coercion system.
    /// Provides fallback mechanism and optimal performance.
    /// </summary>
    public bool TryConfigureParameterEnhanced(DbParameter parameter, Type clrType, object? value,
        SupportedDatabase provider)
    {
        // First try the legacy advanced type system for backward compatibility
        if (TryConfigureParameter(parameter, clrType, value, provider))
        {
            return true;
        }

        // Fall back to the new coercion system for "weird" types
        if (ProviderParameterFactory.TryConfigureParameter(parameter, clrType, value, provider))
        {
            return true;
        }

        // Final fallback: try parameter binding rules
        return ParameterBindingRules.ApplyBindingRules(parameter, clrType, value, provider);
    }

    /// <summary>
    /// Get the coercion registry for direct access to weird type handling.
    /// </summary>
    // Compatibility property: the application-wide facade exposes the shared
    // coercion registry. Instance-local registries remain isolated for tests and
    // internal custom mapping scenarios.
    public CoercionRegistry CoercionRegistry => CoercionRegistry.Shared;

    private void RegisterDefaultMappings()
    {
        // JSON/JSONB types (already well implemented, but formalize here)
        RegisterJsonMappings();

        // Spatial types
        RegisterSpatialMappings();

        // Array types
        RegisterArrayMappings();

        // Range types
        RegisterRangeMappings();

        // Network types
        RegisterNetworkMappings();

        // Temporal types
        RegisterTemporalMappings();

        // LOB types
        RegisterLobMappings();

        // Identity/Concurrency types
        RegisterIdentityMappings();

        // Snowflake-specific types
        RegisterSnowflakeMappings();

        // SQLite-specific types
        RegisterSqliteMappings();

        // Oracle-specific types
        RegisterOracleMappings();

        // Fallback mappings for Unknown/SQL-92
        RegisterFallbackMappings();
    }

    private void RegisterFallbackMappings()
    {
        // For Unknown/Fallback dialects, we use the most compatible formats
        // (doubles for decimals) which works for most lightweight providers.

        RegisterMapping<decimal>(SupportedDatabase.Unknown, new ProviderTypeMapping
        {
            DbType = DbType.Double,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.Double;
                if (value != null)
                {
                    decimal dec = value is decimal d ? d : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    param.Value = (double)dec;
                    var (p, s) = DecimalHelpers.Infer(dec);
                    param.Precision = (byte)Math.Max(p, 18);
                    param.Scale = (byte)s;
                }
            }
        });
    }

    private void RegisterSqliteMappings()
    {
        // SQLite: store decimals as Double (REAL)
        RegisterMapping<decimal>(SupportedDatabase.Sqlite, new ProviderTypeMapping
        {
            DbType = DbType.Double,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.Double;
                if (value != null)
                {
                    decimal dec;
                    if (value is decimal d)
                    {
                        dec = d;
                    }
                    else
                    {
                        dec = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    }

                    param.Value = (double)dec;

                    // Maintain Precision/Scale metadata even when storing as double
                    // to satisfy unit tests and provide consistent parameter shapes.
                    var (inferredPrecision, inferredScale) = DecimalHelpers.Infer(dec);
                    param.Precision = (byte)Math.Max(inferredPrecision, 18);
                    param.Scale = (byte)inferredScale;
                }
            }
        });

        // SQLite: store byte arrays as BLOB
        RegisterMapping<byte[]>(SupportedDatabase.Sqlite, new ProviderTypeMapping
        {
            DbType = DbType.Binary,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.Binary;
                if (value is byte[] bytes)
                {
                    param.Value = bytes;
                    param.Size = bytes.Length;
                }
            }
        });
    }

    private void RegisterOracleMappings()
    {
        // Oracle: map bool to NUMBER(1) via Int16
        RegisterMapping<bool>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Int16,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.Int16;
                if (value is bool b)
                {
                    param.Value = b ? 1 : 0;
                }
            }
        });

        // Oracle Guid: handled by OracleDialect.GuidFormat (GuidStorageFormat.String).
        // Removed from AdvancedTypeRegistry to keep Guid handling dialect-co-located.
    }

    private void RegisterDefaultConverters()
    {
        // Spatial converters
        RegisterConverter(new GeometryConverter());
        RegisterConverter(new GeographyConverter());

        // Range converters
        RegisterConverter(new PostgreSqlRangeConverter<int>());
        RegisterConverter(new PostgreSqlRangeConverter<DateTime>());
        RegisterConverter(new PostgreSqlRangeConverter<long>());

        // Network converters
        RegisterConverter(new InetConverter());
        RegisterConverter(new CidrConverter());
        RegisterConverter(new MacAddressConverter());

        // Interval converters
        RegisterConverter(new PostgreSqlIntervalConverter());
        RegisterConverter(new IntervalYearMonthConverter());
        RegisterConverter(new IntervalDaySecondConverter());

        // Concurrency tokens
        RegisterConverter(new RowVersionConverter());

        // LOB converters
        RegisterConverter(new BlobStreamConverter());
        RegisterConverter(new ClobStreamConverter());

        // JSON converters
        RegisterConverter(new JsonDocumentConverter());
    }

    private void RegisterJsonMappings()
    {
        // PostgreSQL JSONB (shared by flavors)
        var pgJson = new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                param.GetType().GetProperty(NpgsqlNames.DataTypeName)?.SetValue(param, "jsonb");
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Jsonb);
            }
        };
        RegisterMapping<JsonDocument>(SupportedDatabase.PostgreSql, pgJson);
        RegisterMapping<JsonDocument>(SupportedDatabase.CockroachDb, pgJson);
        RegisterMapping<JsonDocument>(SupportedDatabase.YugabyteDb, pgJson);

        // MySQL JSON (shared by flavors)
        var mySqlJson = new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, MySqlNames.DbTypeProperty, MySqlNames.Json);
            }
        };
        RegisterMapping<JsonDocument>(SupportedDatabase.MySql, mySqlJson);
        RegisterMapping<JsonDocument>(SupportedDatabase.TiDb, mySqlJson);

        // SQL Server JSON (stored as NVARCHAR(MAX))
        RegisterMapping<JsonDocument>(SupportedDatabase.SqlServer, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                param.Size = -1; // NVARCHAR(MAX)
            }
        });
    }

    private void RegisterSpatialMappings()
    {
        // SQL Server Geometry
        RegisterMapping<Geometry>(SupportedDatabase.SqlServer, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, SqlServerNames.DbTypeProperty, SqlServerNames.Udt);
                param.GetType().GetProperty(SqlServerNames.UdtTypeName)?.SetValue(param, "geometry");
            }
        });

        // SQL Server Geography
        RegisterMapping<Geography>(SupportedDatabase.SqlServer, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, SqlServerNames.DbTypeProperty, SqlServerNames.Udt);
                param.GetType().GetProperty(SqlServerNames.UdtTypeName)?.SetValue(param, "geography");
            }
        });

        // PostgreSQL PostGIS Geometry
        RegisterMapping<Geometry>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.Binary,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.Binary;
                // Value should be converted to WKB by converter
            }
        });
    }

    private void RegisterArrayMappings()
    {
        // PostgreSQL int[] arrays
        var pgIntArray = new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Array, NpgsqlNames.Integer);
            }
        };
        RegisterMapping<int[]>(SupportedDatabase.PostgreSql, pgIntArray);
        RegisterMapping<int[]>(SupportedDatabase.CockroachDb, pgIntArray);
        RegisterMapping<int[]>(SupportedDatabase.YugabyteDb, pgIntArray);

        // PostgreSQL text[] arrays
        var pgTextArray = new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Array, NpgsqlNames.Text);
            }
        };
        RegisterMapping<string[]>(SupportedDatabase.PostgreSql, pgTextArray);
        RegisterMapping<string[]>(SupportedDatabase.CockroachDb, pgTextArray);
        RegisterMapping<string[]>(SupportedDatabase.YugabyteDb, pgTextArray);
    }

    private void RegisterRangeMappings()
    {
        // PostgreSQL int4range
        var pgIntRange = new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Int4Range);
            }
        };
        RegisterMapping<Range<int>>(SupportedDatabase.PostgreSql, pgIntRange);
        RegisterMapping<Range<int>>(SupportedDatabase.CockroachDb, pgIntRange);
        RegisterMapping<Range<int>>(SupportedDatabase.YugabyteDb, pgIntRange);

        // PostgreSQL tsrange
        var pgTsRange = new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.TsRange);
            }
        };
        RegisterMapping<Range<DateTime>>(SupportedDatabase.PostgreSql, pgTsRange);
        RegisterMapping<Range<DateTime>>(SupportedDatabase.CockroachDb, pgTsRange);
        RegisterMapping<Range<DateTime>>(SupportedDatabase.YugabyteDb, pgTsRange);

        // PostgreSQL int8range
        var pgLongRange = new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Int8Range);
            }
        };
        RegisterMapping<Range<long>>(SupportedDatabase.PostgreSql, pgLongRange);
        RegisterMapping<Range<long>>(SupportedDatabase.CockroachDb, pgLongRange);
        RegisterMapping<Range<long>>(SupportedDatabase.YugabyteDb, pgLongRange);
    }

    private void RegisterNetworkMappings()
    {
        // PostgreSQL inet
        var pgInet = new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Inet);
            }
        };
        RegisterMapping<Inet>(SupportedDatabase.PostgreSql, pgInet);
        RegisterMapping<Inet>(SupportedDatabase.CockroachDb, pgInet);
        RegisterMapping<Inet>(SupportedDatabase.YugabyteDb, pgInet);

        // PostgreSQL cidr
        var pgCidr = new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Cidr);
            }
        };
        RegisterMapping<Cidr>(SupportedDatabase.PostgreSql, pgCidr);
        RegisterMapping<Cidr>(SupportedDatabase.CockroachDb, pgCidr);
        RegisterMapping<Cidr>(SupportedDatabase.YugabyteDb, pgCidr);

        // PostgreSQL macaddr
        var pgMac = new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.MacAddr);
            }
        };
        RegisterMapping<MacAddress>(SupportedDatabase.PostgreSql, pgMac);
        RegisterMapping<MacAddress>(SupportedDatabase.CockroachDb, pgMac);
        RegisterMapping<MacAddress>(SupportedDatabase.YugabyteDb, pgMac);
    }

    private void RegisterTemporalMappings()
    {
        // PostgreSQL interval
        var pgInterval = new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Interval);
            }
        };
        RegisterMapping<PostgreSqlInterval>(SupportedDatabase.PostgreSql, pgInterval);
        RegisterMapping<PostgreSqlInterval>(SupportedDatabase.CockroachDb, pgInterval);
        RegisterMapping<PostgreSqlInterval>(SupportedDatabase.YugabyteDb, pgInterval);

        RegisterMapping<IntervalYearMonth>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, OracleNames.DbTypeProperty, OracleNames.IntervalYM);
            }
        });

        RegisterMapping<IntervalDaySecond>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, OracleNames.DbTypeProperty, OracleNames.IntervalDS);
            }
        });

        // SQL Server DateTimeOffset (UTC policy)
        RegisterMapping<DateTimeOffset>(SupportedDatabase.SqlServer, new ProviderTypeMapping
        {
            DbType = DbType.DateTimeOffset,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.DateTimeOffset;
                // Note: The value arriving here is typed as DateTimeOffset (the registered CLR type),
                // so a `value is DateTime` branch can never match and has been removed.
            }
        });

        // Oracle DateTimeOffset uses TIMESTAMP WITH TIME ZONE (OracleDbType.TimeStampTZ)
        RegisterMapping<DateTimeOffset>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                if (value is DateTimeOffset dto)
                {
                    // Normalize to UTC to avoid offset loss on round-trip.
                    param.Value = dto.ToUniversalTime();
                }
                SetEnumProperty(param, OracleNames.DbTypeProperty, OracleNames.TimeStampTZ);
            }
        });
    }

    private void RegisterLobMappings()
    {
        // SQL Server varbinary(max)
        RegisterMapping<Stream>(SupportedDatabase.SqlServer, new ProviderTypeMapping
        {
            DbType = DbType.Binary,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.Binary;
                param.Size = -1; // varbinary(max)
            }
        });

        RegisterMapping<TextReader>(SupportedDatabase.SqlServer, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                param.Size = -1; // nvarchar(max)
            }
        });

        // PostgreSQL bytea
        var pgStream = new ProviderTypeMapping
        {
            DbType = DbType.Binary,
            ConfigureParameter = (param, value) => { param.DbType = DbType.Binary; }
        };
        RegisterMapping<Stream>(SupportedDatabase.PostgreSql, pgStream);
        RegisterMapping<Stream>(SupportedDatabase.CockroachDb, pgStream);
        RegisterMapping<Stream>(SupportedDatabase.YugabyteDb, pgStream);

        var pgTextReader = new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Text);
            }
        };
        RegisterMapping<TextReader>(SupportedDatabase.PostgreSql, pgTextReader);
        RegisterMapping<TextReader>(SupportedDatabase.CockroachDb, pgTextReader);
        RegisterMapping<TextReader>(SupportedDatabase.YugabyteDb, pgTextReader);

        // Oracle BLOB
        RegisterMapping<Stream>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Binary,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, OracleNames.DbTypeProperty, OracleNames.Blob);
            }
        });

        RegisterMapping<TextReader>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, OracleNames.DbTypeProperty, OracleNames.Clob);
            }
        });
    }

    private void RegisterIdentityMappings()
    {
        // SQL Server rowversion
        RegisterMapping<RowVersion>(SupportedDatabase.SqlServer, new ProviderTypeMapping
        {
            DbType = DbType.Binary,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.Binary;
                param.Size = 8;
                SetEnumProperty(param, SqlServerNames.DbTypeProperty, SqlServerNames.Timestamp);
            }
        });

        // PostgreSQL UUID
        var pgGuid = new ProviderTypeMapping
        {
            DbType = DbType.Guid,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Uuid);
            }
        };
        RegisterMapping<Guid>(SupportedDatabase.PostgreSql, pgGuid);
        RegisterMapping<Guid>(SupportedDatabase.CockroachDb, pgGuid);
        RegisterMapping<Guid>(SupportedDatabase.YugabyteDb, pgGuid);
    }

    private void RegisterSnowflakeMappings()
    {
        // Snowflake BINARY / VARBINARY columns via Stream
        RegisterMapping<Stream>(SupportedDatabase.Snowflake, new ProviderTypeMapping
        {
            DbType = DbType.Binary,
            ConfigureParameter = (param, value) => { param.DbType = DbType.Binary; }
        });

        // Snowflake TIMESTAMP_NTZ for DateTimeOffset (store UTC DateTime)
        RegisterMapping<DateTimeOffset>(SupportedDatabase.Snowflake, new ProviderTypeMapping
        {
            DbType = DbType.DateTime,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.DateTime;
                if (value is DateTimeOffset dto)
                {
                    param.Value = dto.UtcDateTime;
                }
            }
        });

        // Snowflake Guid: handled by SnowflakeDialect.GuidFormat (GuidStorageFormat.String).
        // Removed from AdvancedTypeRegistry to keep Guid handling dialect-co-located.
    }

    private static void SetEnumProperty(DbParameter parameter, string propertyName, params string[] enumNames)
    {
        if (parameter == null || string.IsNullOrEmpty(propertyName) || enumNames.Length == 0)
        {
            return;
        }

        var paramType = parameter.GetType();
        var cacheKey = (paramType, propertyName);

        // Use cached PropertyInfo lookup
        var property = PropertyCache.GetOrAdd(cacheKey, static k => k.Item1.GetProperty(k.Item2));
        if (property == null)
        {
            return;
        }

        var enumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (!enumType.IsEnum)
        {
            return;
        }

        var enumValue = GetEnumValue(enumType, enumNames);
        if (enumValue != null)
        {
            property.SetValue(parameter, enumValue);
        }
    }

    private static object? GetEnumValue(Type enumType, string[] enumNames)
    {
        if (enumNames.Length == 1)
        {
            var cacheKey = (enumType, enumNames[0]);
            return EnumCache.GetOrAdd(cacheKey, static k =>
                Enum.TryParse(k.Item1, k.Item2, true, out var parsed) ? parsed : null);
        }

        // For combined flags, build a composite cache key
        var combinedKey = (enumType, string.Join("|", enumNames));
        return EnumCache.GetOrAdd(combinedKey, k =>
        {
            long combined = 0;
            var names = k.Item2.Split('|');
            foreach (var name in names)
            {
                if (!Enum.TryParse(k.Item1, name, true, out var parsedPart))
                {
                    return null;
                }

                combined |= Convert.ToInt64(parsedPart);
            }

            return Enum.ToObject(k.Item1, combined);
        });
    }
}

/// <summary>
/// Provider-specific type mapping configuration.
/// </summary>
internal class ProviderTypeMapping
{
    public DbType DbType { get; init; }
    public Action<DbParameter, object?>? ConfigureParameter { get; init; }
}
