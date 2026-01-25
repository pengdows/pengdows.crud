using System.Data;
using System.Data.Common;
using System.Text.Json;
using pengdows.crud.enums;
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
        ClrType = clrType;
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
/// </summary>
internal readonly struct CachedParameterConfig
{
    public readonly ProviderTypeMapping Mapping;
    public readonly IAdvancedTypeConverter? Converter;

    public CachedParameterConfig(ProviderTypeMapping mapping, IAdvancedTypeConverter? converter)
    {
        Mapping = mapping;
        Converter = converter;
    }
}

/// <summary>
/// Registry for advanced database type mappings across different providers.
/// Handles spatial, JSON, arrays, ranges, network types, etc.
/// </summary>
public class AdvancedTypeRegistry
{
    public static AdvancedTypeRegistry Shared { get; } = new(true);

    private readonly Dictionary<MappingKey, ProviderTypeMapping> _mappings = new();
    private readonly Dictionary<Type, IAdvancedTypeConverter> _converters = new();
    private readonly HashSet<Type> _mappedTypes = new();

    // Performance cache for frequently accessed combinations
    private readonly Dictionary<MappingKey, CachedParameterConfig?> _parameterCache = new();

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
        var key = new MappingKey(typeof(T), provider);
        _mappings[key] = mapping;
        _mappedTypes.Add(typeof(T));

        // Clear any cached config for this type to force rebuild
        _parameterCache.Remove(key);
    }

    /// <summary>
    /// Register a converter for complex type transformations.
    /// </summary>
    public void RegisterConverter<T>(AdvancedTypeConverter<T> converter)
    {
        var type = typeof(T);
        _converters[type] = converter;

        // Clear any cached configs for this type across all providers
        var keysToRemove = new List<MappingKey>();
        foreach (var key in _parameterCache.Keys)
        {
            if (key.ClrType == type)
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _parameterCache.Remove(key);
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
    /// High-performance version with caching to avoid repeated lookups.
    /// </summary>
    public bool TryConfigureParameter(DbParameter parameter, Type clrType, object? value, SupportedDatabase provider)
    {
        var key = new MappingKey(clrType, provider);

        // Try cached config first for best performance
        if (!_parameterCache.TryGetValue(key, out var cachedConfig))
        {
            // Build and cache the configuration
            var mapping = _mappings.TryGetValue(key, out var foundMapping) ? foundMapping : null;
            if (mapping == null)
            {
                _parameterCache[key] = null; // Cache negative result
                return false;
            }

            var initialConverter = _converters.TryGetValue(clrType, out var foundConverter) ? foundConverter : null;
            cachedConfig = new CachedParameterConfig(mapping, initialConverter);
            _parameterCache[key] = cachedConfig;
        }

        if (cachedConfig == null)
        {
            return false;
        }

        var config = cachedConfig.Value;
        var converter = config.Converter;

        if (_converters.TryGetValue(clrType, out var latestConverter))
        {
            if (!ReferenceEquals(converter, latestConverter))
            {
                converter = latestConverter;
                _parameterCache[key] = new CachedParameterConfig(config.Mapping, converter);
            }
        }
        else if (converter != null)
        {
            converter = null;
            _parameterCache[key] = new CachedParameterConfig(config.Mapping, null);
        }

        // Apply converter if present and value is not null
        if (converter != null && value != null)
        {
            value = converter.ToProviderValue(value, provider);
            System.Diagnostics.Debug.WriteLine(
                $"AdvancedTypeRegistry: converted {clrType.Name} for {provider} to {value?.GetType().FullName ?? "null"}");
        }

        parameter.Value = value ?? DBNull.Value;
        parameter.DbType = config.Mapping.DbType;

        // Apply provider-specific configuration
        config.Mapping.ConfigureParameter?.Invoke(parameter, value);

        return true;
    }

    internal bool IsMappedType(Type clrType)
    {
        return _mappedTypes.Contains(clrType);
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
    }

    private void RegisterJsonMappings()
    {
        // PostgreSQL JSONB
        RegisterMapping<JsonDocument>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                param.GetType().GetProperty("DataTypeName")?.SetValue(param, "jsonb");
                SetEnumProperty(param, "NpgsqlDbType", "Jsonb");
            }
        });

        // MySQL JSON
        RegisterMapping<JsonDocument>(SupportedDatabase.MySql, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "MySqlDbType", "JSON"); }
        });

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
                SetEnumProperty(param, "SqlDbType", "Udt");
                param.GetType().GetProperty("UdtTypeName")?.SetValue(param, "geometry");
            }
        });

        // SQL Server Geography
        RegisterMapping<Geography>(SupportedDatabase.SqlServer, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                SetEnumProperty(param, "SqlDbType", "Udt");
                param.GetType().GetProperty("UdtTypeName")?.SetValue(param, "geography");
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
        RegisterMapping<int[]>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "NpgsqlDbType", "Array", "Integer"); }
        });

        // PostgreSQL text[] arrays
        RegisterMapping<string[]>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "NpgsqlDbType", "Array", "Text"); }
        });
    }

    private void RegisterRangeMappings()
    {
        // PostgreSQL int4range
        RegisterMapping<Range<int>>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "NpgsqlDbType", "Int4Range"); }
        });

        // PostgreSQL tsrange
        RegisterMapping<Range<DateTime>>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "NpgsqlDbType", "TsRange"); }
        });
    }

    private void RegisterNetworkMappings()
    {
        // PostgreSQL inet
        RegisterMapping<Inet>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "NpgsqlDbType", "Inet"); }
        });

        // PostgreSQL cidr
        RegisterMapping<Cidr>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "NpgsqlDbType", "Cidr"); }
        });

        // PostgreSQL macaddr
        RegisterMapping<MacAddress>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "NpgsqlDbType", "MacAddr"); }
        });
    }

    private void RegisterTemporalMappings()
    {
        // PostgreSQL interval
        RegisterMapping<PostgreSqlInterval>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "NpgsqlDbType", "Interval"); }
        });

        RegisterMapping<IntervalYearMonth>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "OracleDbType", "IntervalYM"); }
        });

        RegisterMapping<IntervalDaySecond>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "OracleDbType", "IntervalDS"); }
        });

        // SQL Server DateTimeOffset (UTC policy)
        RegisterMapping<DateTimeOffset>(SupportedDatabase.SqlServer, new ProviderTypeMapping
        {
            DbType = DbType.DateTimeOffset,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.DateTimeOffset;
                if (value is DateTime dt && dt.Kind == DateTimeKind.Unspecified)
                {
                    throw new InvalidOperationException(
                        "DateTime with Kind=Unspecified not allowed. Use DateTimeOffset or specify Kind.");
                }
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
        RegisterMapping<Stream>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.Binary,
            ConfigureParameter = (param, value) => { param.DbType = DbType.Binary; }
        });

        RegisterMapping<TextReader>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                SetEnumProperty(param, "NpgsqlDbType", "Text");
            }
        });

        // Oracle BLOB
        RegisterMapping<Stream>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Binary,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "OracleDbType", "Blob"); }
        });

        RegisterMapping<TextReader>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "OracleDbType", "Clob"); }
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
                SetEnumProperty(param, "SqlDbType", "Timestamp");
            }
        });

        // PostgreSQL UUID
        RegisterMapping<Guid>(SupportedDatabase.PostgreSql, new ProviderTypeMapping
        {
            DbType = DbType.Guid,
            ConfigureParameter = (param, value) => { SetEnumProperty(param, "NpgsqlDbType", "Uuid"); }
        });
    }

    private static void SetEnumProperty(DbParameter parameter, string propertyName, params string[] enumNames)
    {
        if (parameter == null || string.IsNullOrEmpty(propertyName) || enumNames.Length == 0)
        {
            return;
        }

        var property = parameter.GetType().GetProperty(propertyName);
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
            return Enum.TryParse(enumType, enumNames[0], true, out var parsedSingle)
                ? parsedSingle
                : null;
        }

        long combined = 0;
        foreach (var name in enumNames)
        {
            if (!Enum.TryParse(enumType, name, true, out var parsedPart))
            {
                return null;
            }

            combined |= Convert.ToInt64(parsedPart);
        }

        return Enum.ToObject(enumType, combined);
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