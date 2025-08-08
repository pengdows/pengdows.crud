#region

using System.Collections;

#endregion

namespace pengdows.crud;

public class Utils
{
    public static bool IsNullOrDbNull(object? value)
    {
        return value == null || value is DBNull;
    }

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

    public static bool IsNullOrEmpty<T>(IEnumerable<T>? collection)
    {
        if (collection is null) return true;

        // If it's a known countable type, use .Count
        if (collection is ICollection c) return c.Count == 0;
        if (collection is ICollection<T> gc) return gc.Count == 0;
        if (collection is IReadOnlyCollection<T> rc) return rc.Count == 0;

        // Otherwise enumerate once
        return !collection.Any();
    }
}