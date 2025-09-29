using System;
using System.Collections.Concurrent;
using System.Data.Common;
using pengdows.crud.enums;

namespace pengdows.crud.types.coercion;

/// <summary>
/// High-performance registry for database type coercions.
/// Thread-safe and optimized for frequent lookups.
/// </summary>
public class CoercionRegistry
{
    public static CoercionRegistry Shared { get; } = new();

    private readonly ConcurrentDictionary<Type, IDbCoercion> _coercions = new();
    private readonly ConcurrentDictionary<(Type, SupportedDatabase), IDbCoercion> _providerSpecificCoercions = new();

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
                return providerCoercion;
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
        // TODO: Fix SimpleCoercions signature issues and re-enable
        // SimpleCoercions.RegisterAll(this);

        // TODO: Re-enable advanced coercions once signature issues are resolved
        // Tier0Coercions.RegisterAll(this);
        // ProviderSpecificCoercions.RegisterAll(this);
    }
}

/// <summary>
/// Base class for strongly-typed coercions to reduce boilerplate.
/// </summary>
public abstract class DbCoercion<T> : IDbCoercion<T>
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
            if (TryRead(src, out T? typedValue))
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