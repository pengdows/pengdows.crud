// =============================================================================
// FILE: PostgreSqlMultirange.cs
// PURPOSE: A PostgreSQL multirange (an array of ranges): its typed array for the driver, and its read back as Range values.
//
// AI SUMMARY:
// - TypedArray(): the ranges as an array of the driver's range type (the converter's NpgsqlRange when Npgsql is loaded),
//   so the parameter is typed as a multirange rather than an untyped array.
// - PostgreSqlMultirangeCoercion: the driver's array of ranges (each element converted by the range converter) or a
//   string of ranges separated by semicolons, to Range<int>[].
// =============================================================================

using System;
using System.Collections.Generic;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.coercion;

internal static class PostgreSqlMultirange
{
    /// <summary>The converted ranges as an array typed to their common type (the driver's range type).</summary>
    internal static Array TypedArray(IReadOnlyList<object?> converted)
    {
        Type elementType = typeof(object);
        foreach (var item in converted)
        {
            if (item is not null)
            {
                elementType = item.GetType();
                break;
            }
        }

        var array = Array.CreateInstance(elementType, converted.Count);
        for (var i = 0; i < converted.Count; i++)
        {
            array.SetValue(converted[i], i);
        }

        return array;
    }
}

/// <summary>Reads a PostgreSQL multirange: the driver's array of ranges, or semicolon-separated range text.</summary>
internal sealed class PostgreSqlMultirangeCoercion : DbCoercion<Range<int>[]>
{
    private readonly SupportedDatabase _provider;

    public PostgreSqlMultirangeCoercion(SupportedDatabase provider) => _provider = provider;

    public override bool TryRead(in DbValue src, out Range<int>[]? value)
    {
        if (src.IsNull)
        {
            value = null;
            return false;
        }

        switch (src.RawValue)
        {
            case Range<int>[] ranges:
                value = ranges;
                return true;
            case Array items:
                var converter = new PostgreSqlRangeConverter<int>();
                var result = new Range<int>[items.Length];
                for (var i = 0; i < items.Length; i++)
                {
                    if (!converter.TryConvertFromProvider(items.GetValue(i)!, _provider, out result[i]))
                    {
                        value = null;
                        return false;
                    }
                }

                value = result;
                return true;
            default:
                return new RangeArrayCoercion().TryRead(in src, out value);
        }
    }
}
