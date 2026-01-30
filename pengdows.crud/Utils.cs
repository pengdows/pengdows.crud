// =============================================================================
// FILE: Utils.cs
// PURPOSE: General-purpose utility methods used throughout the library for
//          null checking, numeric validation, and collection emptiness tests.
//
// AI SUMMARY:
// - IsNullOrDbNull: Checks if a value is null or DBNull.Value (common in
//   ADO.NET when reading from DataReader).
// - IsZeroNumeric: Checks if a value is a numeric zero across all numeric
//   types (byte through decimal, including floating point).
// - IsNullOrEmpty<T>: Checks if a collection is null or empty, optimized
//   to use Count property when available to avoid enumeration.
// - These utilities handle the common patterns when working with database
//   values that might be null, DBNull, or empty collections.
// =============================================================================

#region

using System.Collections;

#endregion

namespace pengdows.crud;

/// <summary>
/// General-purpose utility methods for null checking and value validation.
/// </summary>
/// <remarks>
/// These utilities handle common patterns when working with ADO.NET and database values,
/// particularly the distinction between null references and <see cref="DBNull.Value"/>.
/// </remarks>
public class Utils
{
    /// <summary>
    /// Determines whether the specified value is null or <see cref="DBNull.Value"/>.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>
    /// <c>true</c> if <paramref name="value"/> is null or <see cref="DBNull.Value"/>; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// This is a common check when reading values from <see cref="System.Data.IDataReader"/>
    /// where NULL columns return <see cref="DBNull.Value"/> rather than null.
    /// </remarks>
    public static bool IsNullOrDbNull(object? value)
    {
        return value == null || value is DBNull;
    }

    /// <summary>
    /// Determines whether the specified value is a numeric zero.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>
    /// <c>true</c> if <paramref name="value"/> is a numeric type with value zero; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// Handles all .NET numeric types: byte, sbyte, short, ushort, int, uint, long, ulong,
    /// float, double, and decimal.
    /// </remarks>
    public static bool IsZeroNumeric(object value)
    {
        return value switch
        {
            byte b => b == 0,
            sbyte sb => sb == 0,
            short s => s == 0,
            ushort us => us == 0,
            int i => i == 0,
            uint ui => ui == 0,
            long l => l == 0,
            ulong ul => ul == 0,
            float f => f == 0f,
            double d => d == 0d,
            decimal m => m == 0m,
            _ => false
        };
    }

    /// <summary>
    /// Determines whether the specified collection is null or empty.
    /// </summary>
    /// <typeparam name="T">The element type of the collection.</typeparam>
    /// <param name="collection">The collection to check.</param>
    /// <returns>
    /// <c>true</c> if <paramref name="collection"/> is null or contains no elements; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// This method is optimized to use the <c>Count</c> property when the collection
    /// implements <see cref="ICollection"/>, <see cref="ICollection{T}"/>, or
    /// <see cref="IReadOnlyCollection{T}"/> to avoid enumeration overhead.
    /// </remarks>
    public static bool IsNullOrEmpty<T>(IEnumerable<T>? collection)
    {
        if (collection is null)
        {
            return true;
        }

        // If it's a known countable type, use .Count
        if (collection is ICollection c)
        {
            return c.Count == 0;
        }

        if (collection is ICollection<T> gc)
        {
            return gc.Count == 0;
        }

        if (collection is IReadOnlyCollection<T> rc)
        {
            return rc.Count == 0;
        }

        // Otherwise enumerate once
        return !collection.Any();
    }

    /// <summary>
    /// Determines whether the specified value is null or <see cref="DBNull.Value"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="arg">The value to check.</param>
    /// <returns>
    /// <c>true</c> if <paramref name="arg"/> is null or <see cref="DBNull.Value"/>; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// Generic version that avoids boxing for value types when possible.
    /// </remarks>
    public static bool IsNullOrDbNull<T>(T arg)
    {
        if (arg == null)
        {
            return true;
        }

        return arg is DBNull;
    }
}