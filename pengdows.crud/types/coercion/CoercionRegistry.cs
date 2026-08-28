// =============================================================================
// FILE: CoercionRegistry.cs
// PURPOSE: Thread-safe registry for database type coercions.
//
// AI SUMMARY:
// - High-performance, thread-safe registry for IDbCoercion implementations.
// - CoercionRegistry.Shared provides singleton with standard coercions registered.
// - Uses ConcurrentDictionary for both general and provider-specific coercions.
// - Register<T>(): Registers coercion for a CLR type (optionally provider-specific).
// - GetCoercion(): Retrieves coercion, preferring provider-specific if available.
// - TryRead(): Converts DbValue to target type using registered coercion.
// - TryWrite(): Configures DbParameter using registered coercion.
// - RegisterStandardCoercions(): Calls BasicCoercions + AdvancedCoercions.RegisterAll().
// - DbCoercion<T>: Abstract base class reducing boilerplate for implementations.
// =============================================================================

using System.Collections.Concurrent;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types;
using pengdows.crud.types.converters;

namespace pengdows.crud.types.coercion;

/// <summary>
/// High-performance registry for database type coercions.
/// Thread-safe and optimized for frequent lookups.
/// </summary>
internal class CoercionRegistry
{
    public static CoercionRegistry Shared { get; } = new();

    private readonly ConcurrentDictionary<Type, IDbCoercion> _coercions = new();
    private readonly ConcurrentDictionary<(Type, SupportedDatabase), IDbCoercion> _providerSpecificCoercions = new();
    private readonly ConcurrentDictionary<(Type, SupportedDatabase), ProviderTypeMapping> _legacyMappings = new();
    private readonly ConcurrentDictionary<Type, IAdvancedTypeConverter> _legacyConverters = new();
    private readonly ConcurrentDictionary<Type, byte> _legacyMappedTypes = new();

    public CoercionRegistry()
    {
        RegisterStandardCoercions();
    }

    /// <summary>
    /// Register a coercion for a specific type.
    /// </summary>
    public void Register<T>(IDbCoercion<T> coercion)
    {
        _coercions[typeof(T)] = coercion;
    }

    /// <summary>
    /// Register a provider-specific coercion for a type.
    /// </summary>
    public void Register<T>(SupportedDatabase provider, IDbCoercion<T> coercion)
    {
        _providerSpecificCoercions[(typeof(T), provider)] = coercion;
    }

    // Compatibility registration for the original provider-mapping API. The
    // mapping lives here so legacy and coercion-based conversions share one
    // registry and one lookup owner.
    public void RegisterMapping<T>(SupportedDatabase provider, ProviderTypeMapping mapping)
    {
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        _legacyMappings[(type, provider)] = mapping;
        _legacyMappedTypes[type] = 0;
    }

    public ProviderTypeMapping? GetMapping(Type type, SupportedDatabase provider)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return _legacyMappings.TryGetValue((type, provider), out var mapping) ? mapping : null;
    }

    public void RegisterConverter<T>(AdvancedTypeConverter<T> converter)
    {
        _legacyConverters[typeof(T)] = converter;
    }

    public IAdvancedTypeConverter? GetConverter(Type type)
    {
        return _legacyConverters.TryGetValue(type, out var converter) ? converter : null;
    }

    public bool IsLegacyMappedType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return _legacyMappedTypes.ContainsKey(type);
    }

    public bool TryConfigureLegacyParameter(
        DbParameter parameter,
        Type type,
        object? value,
        SupportedDatabase provider)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!_legacyMappings.TryGetValue((type, provider), out var mapping))
        {
            return false;
        }

        if (_legacyConverters.TryGetValue(type, out var converter) && value != null)
        {
            value = converter.ToProviderValue(value, provider);
        }

        parameter.DbType = mapping.DbType;
        parameter.Value = value ?? DBNull.Value;
        mapping.ConfigureParameter?.Invoke(parameter, value);
        if (parameter.Value == null || parameter.Value is DBNull)
        {
            parameter.Value = value ?? DBNull.Value;
        }

        return true;
    }

    /// <summary>
    /// Get coercion for a type, optionally provider-specific.
    /// </summary>
    public IDbCoercion? GetCoercion(Type type, SupportedDatabase? provider = null)
    {
        // Try provider-specific first if specified
        if (provider.HasValue)
        {
            var key = (type, provider.Value);
            if (_providerSpecificCoercions.TryGetValue(key, out var providerCoercion))
            {
                return providerCoercion;
            }
        }

        // Fall back to general coercion
        return _coercions.TryGetValue(type, out var coercion) ? coercion : null;
    }

    /// <summary>
    /// Attempt to read a database value using registered coercions.
    /// </summary>
    public bool TryRead(in DbValue src, Type targetType, out object? value, SupportedDatabase? provider = null)
    {
        var coercion = GetCoercion(targetType, provider);
        if (coercion != null)
        {
            return coercion.TryRead(src, targetType, out value);
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Attempt to write a value to a database parameter using registered coercions.
    /// </summary>
    public bool TryWrite(object? value, DbParameter parameter, SupportedDatabase? provider = null)
    {
        if (value == null)
        {
            parameter.Value = DBNull.Value;
            return true;
        }

        var coercion = GetCoercion(value.GetType(), provider);
        if (coercion != null)
        {
            return coercion.TryWrite(value, parameter);
        }

        return false;
    }

    /// <summary>
    /// Register standard "weird" database type coercions.
    /// </summary>
    private void RegisterStandardCoercions()
    {
        // Register basic coercions (primitives, JSON, arrays, ranges)
        BasicCoercions.RegisterAll(this);

        // Register advanced coercions (spatial, network, temporal, LOBs)
        AdvancedCoercions.RegisterAll(this);
    }
}

/// <summary>
/// Base class for strongly-typed coercions to reduce boilerplate.
/// </summary>
internal abstract class DbCoercion<T> : IDbCoercion<T>
{
    public Type TargetType => typeof(T);

    // Match the interface exactly
    public abstract bool TryRead(in DbValue src, out T? value);
    public abstract bool TryWrite(T? value, DbParameter parameter);

    // IDbCoercion implementation
    public bool TryRead(in DbValue src, Type targetType, out object? value)
    {
        if (targetType == typeof(T) || targetType == typeof(T?))
        {
            if (TryRead(src, out var typedValue))
            {
                value = typedValue;
                return true;
            }
        }

        value = null;
        return false;
    }

    public bool TryWrite(object? value, DbParameter parameter)
    {
        if (value is T typedValue)
        {
            return TryWrite(typedValue, parameter);
        }

        if (value == null)
        {
            return TryWrite(default, parameter);
        }

        return false;
    }
}
