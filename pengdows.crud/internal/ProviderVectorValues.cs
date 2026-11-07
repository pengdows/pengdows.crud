// =============================================================================
// FILE: ProviderVectorValues.cs
// PURPOSE: The provider vector values read as arrays: SqlClient's SqlVector<T> and the Pgvector.Npgsql Vector.
//
// AI SUMMARY:
// - TryUnwrap(value, out array): the array a provider vector holds (its memory, or its ToArray()), or false for any
//   other value. The provider types are recognised by their names, because the libraries that define them are optional
//   dependencies this assembly does not reference.
// - The recognised names are kept here, in one place, rather than in the general coercion path.
// =============================================================================

namespace pengdows.crud.@internal;

internal static class ProviderVectorValues
{
    public static bool TryUnwrap(object value,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out object? array)
    {
        array = null;
        var type = value.GetType();
        object? memory = null;
        if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition &&
            (definition == typeof(ReadOnlyMemory<>) || definition == typeof(Memory<>)))
        {
            memory = value;
        }
        else if (type.Name == "SqlVector`1")
        {
            memory = type.GetProperty("Memory")?.GetValue(value);
        }
        else if (type.FullName is "Pgvector.Vector")
        {
            array = type.GetMethod("ToArray", Type.EmptyTypes)?.Invoke(value, null);
            return array != null;
        }

        array = memory?.GetType().GetMethod("ToArray", Type.EmptyTypes)?.Invoke(memory, null);
        return array != null;
    }
}
