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
// - Default mappings (JSON, spatial, arrays, ranges, network, temporal, LOBs, identity) are
//   declared by each dialect class (DatabaseTraits.RegisterTypeMappings, REV-039) and collected
//   here; this file names no database.
// - ProviderTypeMapping: Holds DbType + ConfigureParameter action for provider customization.
// - SetEnumProperty: cached reflection the dialects' mappings use to set provider-specific enum
//   properties (NpgsqlDbType, OracleDbType, etc.).
// - Thread-safe: All mutable collections are ConcurrentDictionary. Converter version stamp
//   avoids per-call dictionary lookup on the hot path.
// =============================================================================

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Reflection;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types.converters;
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
internal class AdvancedTypeRegistry
{
    public static AdvancedTypeRegistry Shared { get; } = new(true);

    private readonly ConcurrentDictionary<MappingKey, ProviderTypeMapping> _mappings = new();
    private readonly ConcurrentDictionary<Type, IAdvancedTypeConverter> _converters = new();
    private readonly ConcurrentDictionary<Type, byte> _mappedTypes = new(); // concurrent hashset pattern

    /// <summary>The types with a converter (for the one-reader-per-type check).</summary>
    internal IEnumerable<Type> ConverterTypes => _converters.Keys;

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
    /// Get the coercion registry for direct access to weird type handling.
    /// </summary>
    public CoercionRegistry CoercionRegistry => CoercionRegistry.Shared;

    // Each database's mappings are declared by its dialect (REV-039: no code outside the dialects
    // names a database); the registry only collects them.
    private void RegisterDefaultMappings()
    {
        foreach (var traits in DatabaseTraits.All)
        {
            traits.RegisterTypeMappings?.Invoke(this);
        }
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
        RegisterConverter(new PostgreSqlRangeConverter<DateOnly>());
        RegisterConverter(new PostgreSqlRangeConverter<decimal>());
        RegisterConverter(new PostgreSqlRangeConverter<DateTimeOffset>());

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

    /// <summary>Sets one enum member: a compiled, cached setter (it runs per parameter).</summary>
    internal static void SetEnumProperty(DbParameter parameter, string propertyName, string enumName)
    {
        if (parameter == null || string.IsNullOrEmpty(propertyName))
        {
            return;
        }

        @internal.ProviderPropertySetter.Set(parameter, propertyName, enumName);
    }

    internal static void SetEnumProperty(DbParameter parameter, string propertyName, params string[] enumNames)
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
