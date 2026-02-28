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

/// <summary>
/// High-performance struct key to avoid tuple allocation in hot paths.
/// </summary>
internal readonly struct MappingKey : IEquatable<MappingKey>
{
    public readonly Type ClrType;
    public readonly SupportedDatabase Provider;

    public MappingKey(Type clrType, SupportedDatabase provider)
    {
        ClrType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        Provider = provider;
    }

    public bool Equals(MappingKey other)
    {
        return ClrType == other.ClrType && Provider == other.Provider;
    }

    public override bool Equals(object? obj)
    {
        return obj is MappingKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ClrType, Provider);
    }
}

/// <summary>
/// Cached configuration for parameter setup to avoid repeated lookups.
/// Includes a converter version stamp to detect stale entries without per-call dictionary lookup.
/// </summary>
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
public class AdvancedTypeRegistry
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
        public const string TsRange = "TsRange";
        public const string Inet = "Inet";
        public const string Cidr = "Cidr";
        public const string MacAddr = "MacAddr";
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

    private readonly ConcurrentDictionary<MappingKey, ProviderTypeMapping> _mappings = new();
    private readonly ConcurrentDictionary<Type, IAdvancedTypeConverter> _converters = new();
    private readonly ConcurrentDictionary<Type, byte> _mappedTypes = new(); // concurrent hashset pattern

    // Performance cache for frequently accessed combinations
    private readonly ConcurrentDictionary<MappingKey, CachedParameterConfig?> _parameterCache = new();

    // Version counter incremented on every RegisterConverter call.
    // On cache hit, a cheap int compare detects stale entries without a dictionary lookup.
    private volatile int _converterVersion;

    // Static reflection caches — reflection results are universal across instances
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();
    private static readonly ConcurrentDictionary<(Type, string), object?> EnumCache = new();

    public AdvancedTypeRegistry(bool includeDefaults = false)
    {
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
        
        var key = new MappingKey(type, provider);
        _mappings[key] = mapping;
        _mappedTypes[type] = 0;

        // Clear any cached config for this key to force rebuild
        _parameterCache.TryRemove(key, out _);
    }

    /// <summary>
    /// Register a converter for complex type transformations.
    /// </summary>
    public void RegisterConverter<T>(AdvancedTypeConverter<T> converter)
    {
        var type = typeof(T);
        _converters[type] = converter;

        // Bump converter version — cached entries with old version will be rebuilt on next access
        Interlocked.Increment(ref _converterVersion);

        // Also remove stale cache entries for this type (belt and suspenders)
        foreach (var key in _parameterCache.Keys)
        {
            if (key.ClrType == type)
            {
                _parameterCache.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Get provider-specific type mapping for a CLR type.
    /// </summary>
    public ProviderTypeMapping? GetMapping(Type clrType, SupportedDatabase provider)
    {
        var key = new MappingKey(clrType, provider);
        return _mappings.TryGetValue(key, out var mapping) ? mapping : null;
    }

    /// <summary>
    /// Get type converter for a CLR type.
    /// </summary>
    public IAdvancedTypeConverter? GetConverter(Type clrType)
    {
        return _converters.TryGetValue(clrType, out var converter) ? converter : null;
    }

    /// <summary>
    /// Configure a DbParameter with provider-specific type information.
    /// High-performance version with caching and version-stamped converter tracking.
    /// </summary>
    public bool TryConfigureParameter(DbParameter parameter, Type clrType, object? value, SupportedDatabase provider)
    {
        // Unwrap nullable to ensure DateTime? matches DateTime mapping, etc.
        clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        
        var key = new MappingKey(clrType, provider);
        var currentVersion = _converterVersion;

        // Try cached config first for best performance
        if (!_parameterCache.TryGetValue(key, out var cachedConfig))
        {
            // Build and cache the configuration
            if (!_mappings.TryGetValue(key, out var foundMapping))
            {
                _parameterCache[key] = null; // Cache negative result
                return false;
            }

            _converters.TryGetValue(clrType, out var initialConverter);
            cachedConfig = new CachedParameterConfig(foundMapping, initialConverter, currentVersion);
            _parameterCache[key] = cachedConfig;
        }

        if (cachedConfig == null)
        {
            return false;
        }

        var config = cachedConfig.Value;
        var converter = config.Converter;

        // Check if converter version is stale — cheap int compare on every call
        // Only do a dictionary lookup when the version mismatches
        if (config.ConverterVersion != currentVersion)
        {
            _converters.TryGetValue(clrType, out var latestConverter);
            converter = latestConverter;
            var updatedConfig = new CachedParameterConfig(config.Mapping, converter, currentVersion);
            _parameterCache[key] = updatedConfig;
        }

        // Apply converter if present and value is not null
        if (converter != null && value != null)
        {
            value = converter.ToProviderValue(value, provider);
            System.Diagnostics.Debug.WriteLine(
                $"AdvancedTypeRegistry: converted {clrType.Name} for {provider} to {value?.GetType().FullName ?? "null"}");
        }

        // Initialize parameter with default mapping values
        parameter.DbType = config.Mapping.DbType;
        parameter.Value = value ?? DBNull.Value;

        // Apply provider-specific configuration
        config.Mapping.ConfigureParameter?.Invoke(parameter, value);

        // Crucial: Update the actual parameter value with the potentially transformed 'value'
        // only if the configuration action didn't already set it.
        if (parameter.Value == null || parameter.Value is DBNull)
        {
            parameter.Value = value ?? DBNull.Value;
        }

        return true;
    }

    internal bool IsMappedType(Type clrType)
    {
        clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        return _mappedTypes.ContainsKey(clrType);
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
                    if (value is decimal d) dec = d;
                    else dec = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    
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

        // Oracle: store GUIDs as VARCHAR2(36)
        RegisterMapping<Guid>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                param.Size = 36;
                if (value is Guid guid)
                {
                    param.Value = guid.ToString("D");
                }
            }
        });
    }

    private void RegisterDefaultConverters()
    {
        // Spatial converters
        RegisterConverter(new GeometryConverter());
        RegisterConverter(new GeographyConverter());

        // Range converters
        RegisterConverter(new PostgreSqlRangeConverter<int>());
        RegisterConverter(new PostgreSqlRangeConverter<DateTime>());

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
            DbType = DbType.String,
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
            DbType = DbType.String,
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
            DbType = DbType.String,
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
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.TsRange);
            }
        };
        RegisterMapping<Range<DateTime>>(SupportedDatabase.PostgreSql, pgTsRange);
        RegisterMapping<Range<DateTime>>(SupportedDatabase.CockroachDb, pgTsRange);
        RegisterMapping<Range<DateTime>>(SupportedDatabase.YugabyteDb, pgTsRange);
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

        // Snowflake stores GUIDs as VARCHAR(36) — use plain string with fixed size
        RegisterMapping<Guid>(SupportedDatabase.Snowflake, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                param.Size = 36;
                if (value is Guid guid)
                {
                    param.Value = guid.ToString("D");
                }
            }
        });
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
public class ProviderTypeMapping
{
    public DbType DbType { get; init; }
    public Action<DbParameter, object?>? ConfigureParameter { get; init; }
}
