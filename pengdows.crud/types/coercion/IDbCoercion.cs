// =============================================================================
// FILE: IDbCoercion.cs
// PURPOSE: Interface for high-performance database type coercion.
//
// AI SUMMARY:
// - Defines contract for converting between database values and .NET types.
// - DbValue: Lightweight readonly struct wrapping raw value + optional DbType.
// - IDbCoercion: Non-generic read interface (TryRead). Coercions only read: parameters are written
//   by the AdvancedTypeRegistry converters and the dialects (DRY-010).
// - IDbCoercion<T>: Generic strongly-typed interface for specific type handling.
// - TryRead(): Convert database value to .NET type (returns success bool).
// - Designed for high performance and AOT compatibility.
// - Implementations: GuidCoercion, BooleanCoercion, JsonValueCoercion, etc.
// =============================================================================

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
internal interface IDbCoercion
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
    /// The .NET type this coercion handles.
    /// </summary>
    Type TargetType { get; }
}

/// <summary>
/// Generic interface for strongly-typed coercions.
/// </summary>
internal interface IDbCoercion<T> : IDbCoercion
{
    /// <summary>
    /// Attempt to read a database value into the target type.
    /// </summary>
    bool TryRead(in DbValue src, out T? value);
}