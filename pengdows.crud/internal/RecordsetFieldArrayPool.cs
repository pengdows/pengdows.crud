// =============================================================================
// FILE: RecordsetFieldArrayPool.cs
// PURPOSE: ArrayPool<string>/ArrayPool<Type> rent/return helpers for reading a recordset's
//          field names/types without allocating on the hot (cache-hit) path.
//
// AI SUMMARY:
// - Used by BaseTableGateway.Reader.cs; keeps the pooling policy (bucket size, max pooled
//   array length) in one place.
// - Arrays sized above FieldPoolMaxLength fall back to ArrayPool<T>.Shared rather than
//   this pool's fixed-size buckets.
// =============================================================================

using System.Buffers;

namespace pengdows.crud.@internal;

internal static class RecordsetFieldArrayPool
{
    private const int FieldPoolMaxLength = 64;
    private const int FieldPoolArraysPerBucket = 32;

    private static readonly ArrayPool<string> FieldNamePool =
        ArrayPool<string>.Create(FieldPoolMaxLength, FieldPoolArraysPerBucket);

    private static readonly ArrayPool<Type> FieldTypePool =
        ArrayPool<Type>.Create(FieldPoolMaxLength, FieldPoolArraysPerBucket);

    public static string[] RentStringArray(int size)
    {
        return size <= FieldPoolMaxLength
            ? FieldNamePool.Rent(size)
            : ArrayPool<string>.Shared.Rent(size);
    }

    public static void ReturnStringArray(string[] array, int size)
    {
        if (size <= FieldPoolMaxLength)
        {
            FieldNamePool.Return(array, clearArray: true);
        }
        else
        {
            ArrayPool<string>.Shared.Return(array, clearArray: true);
        }
    }

    public static Type[] RentTypeArray(int size)
    {
        return size <= FieldPoolMaxLength
            ? FieldTypePool.Rent(size)
            : ArrayPool<Type>.Shared.Rent(size);
    }

    public static void ReturnTypeArray(Type[] array, int size)
    {
        if (size <= FieldPoolMaxLength)
        {
            FieldTypePool.Return(array, clearArray: false);
        }
        else
        {
            ArrayPool<Type>.Shared.Return(array, clearArray: false);
        }
    }
}
