using System;
using System.Data;
using System.Data.Common;

namespace pengdows.crud.types.coercion;

/// <summary>
/// Represents a database value that can be coerced to/from .NET types.
/// High-performance struct to avoid allocations in hot paths.
/// </summary>
public readonly struct DbValue
{
    public readonly object? RawValue;
    public readonly Type? DbType;

    public DbValue(object? rawValue, Type? dbType = null)
    {
        RawValue = rawValue;
        DbType = dbType;
    }

    public bool IsNull => RawValue == null || RawValue == DBNull.Value;

    public T? As<T>()
    {
        return RawValue is T value ? value : default;
    }
}

/// <summary>
/// Interface for type coercion between database values and .NET types.
/// Designed for high-performance, AOT-compatible implementations.
/// </summary>
public interface IDbCoercion
{
    /// <summary>
    /// Attempt to read a database value into a .NET type.
    /// </summary>
    /// <param name="src">The database value to read from</param>
    /// <param name="targetType">The target .NET type</param>
    /// <param name="value">The converted value if successful</param>
    /// <returns>True if conversion succeeded</returns>
    bool TryRead(in DbValue src, Type targetType, out object? value);

    /// <summary>
    /// Attempt to write a .NET value to a database parameter.
    /// </summary>
    /// <param name="value">The .NET value to write</param>
    /// <param name="parameter">The database parameter to configure</param>
    /// <returns>True if parameter was successfully configured</returns>
    bool TryWrite(object? value, DbParameter parameter);

    /// <summary>
    /// The .NET type this coercion handles.
    /// </summary>
    Type TargetType { get; }
}

/// <summary>
/// Generic interface for strongly-typed coercions.
/// </summary>
public interface IDbCoercion<T> : IDbCoercion
{
    /// <summary>
    /// Attempt to read a database value into the target type.
    /// </summary>
    bool TryRead(in DbValue src, out T? value);

    /// <summary>
    /// Write a strongly-typed value to a database parameter.
    /// </summary>
    bool TryWrite(T? value, DbParameter parameter);
}