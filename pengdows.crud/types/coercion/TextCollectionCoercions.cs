// =============================================================================
// FILE: TextCollectionCoercions.cs
// PURPOSE: A list, a string-keyed map and a range array on a database with no such type, stored as text.
//
// AI SUMMARY:
// - ListOfIntCoercion: "1,2,3" <-> List<int>.
// - JsonObjectCoercion: a JSON object <-> Dictionary<string, object> (string, number, boolean and null values).
// - RangeArrayCoercion: "[1, 5);[7, 9)" <-> Range<int>[] (each range by Range<T>.TryParseText).
// - TextCollections: the parameter mappings that write these, and the registration of the coercions (SQLite, DuckDB).
// =============================================================================

using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.coercion;

/// <summary>A list of integers read from its comma-separated text.</summary>
internal sealed class ListOfIntCoercion : DbCoercion<List<int>>
{
    public override bool TryRead(in DbValue src, out List<int>? value)
    {
        if (src.IsNull)
        {
            value = null;
            return false;
        }

        switch (src.RawValue)
        {
            case List<int> list:
                value = list;
                return true;
            case string text:
                return TryParse(text, out value);
            default:
                value = null;
                return false;
        }
    }

    // A JSON array ("[1,2,3]", the form the database's own array text takes) or comma-separated values ("1,2,3").
    private static bool TryParse(string text, out List<int>? value)
    {
        var trimmed = text.Trim();
        try
        {
            if (trimmed.StartsWith('['))
            {
                value = JsonSerializer.Deserialize<List<int>>(trimmed);
                return value != null;
            }

            value = new List<int>();
            foreach (var part in trimmed.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
            {
                value.Add(int.Parse(part.Trim(), CultureInfo.InvariantCulture));
            }

            return true;
        }
        catch (FormatException)
        {
            value = null;
            return false;
        }
        catch (JsonException)
        {
            value = null;
            return false;
        }
    }
}

/// <summary>A string-keyed map read from a JSON object.</summary>
internal sealed class JsonObjectCoercion : DbCoercion<Dictionary<string, object>>
{
    public override bool TryRead(in DbValue src, out Dictionary<string, object>? value)
    {
        if (src.IsNull)
        {
            value = null;
            return false;
        }

        switch (src.RawValue)
        {
            case Dictionary<string, object> map:
                value = map;
                return true;
            case string text:
                using (var document = JsonDocument.Parse(text))
                {
                    value = new Dictionary<string, object>();
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        value[property.Name] = ToValue(property.Value)!;
                    }
                }

                return true;
            default:
                value = null;
                return false;
        }
    }

    private static object? ToValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText(),
    };
}

/// <summary>A range array read from its ranges, separated by semicolons.</summary>
internal sealed class RangeArrayCoercion : DbCoercion<Range<int>[]>
{
    public override bool TryRead(in DbValue src, out Range<int>[]? value)
    {
        if (src.IsNull)
        {
            value = null;
            return false;
        }

        switch (src.RawValue)
        {
            case string text:
                var parts = text.Split(';', System.StringSplitOptions.RemoveEmptyEntries);
                var ranges = new Range<int>[parts.Length];
                for (var i = 0; i < parts.Length; i++)
                {
                    if (!Range<int>.TryParseText(parts[i].Trim(), out ranges[i]))
                    {
                        value = null;
                        return false;
                    }
                }

                value = ranges;
                return true;
            default:
                value = null;
                return false;
        }
    }
}

/// <summary>The parameter mappings and coercions that store the three as text on one database.</summary>
internal static class TextCollections
{
    /// <summary>Writes a list, a map and a range array as their text on <paramref name="provider"/>.</summary>
    internal static void RegisterMappings(AdvancedTypeRegistry registry, SupportedDatabase provider)
    {
        registry.RegisterMapping<List<int>>(provider, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                if (value is List<int> list)
                {
                    param.Value = string.Join(",", list);
                }
            }
        });

        registry.RegisterMapping<Dictionary<string, object>>(provider, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                if (value is Dictionary<string, object> map)
                {
                    param.Value = JsonSerializer.Serialize(map);
                }
            }
        });

        registry.RegisterMapping<Range<int>[]>(provider, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                if (value is Range<int>[] ranges)
                {
                    param.Value = string.Join(";", ranges);
                }
            }
        });
    }

    /// <summary>Reads the three back from their text on <paramref name="provider"/>.</summary>
    internal static void RegisterCoercions(CoercionRegistry registry, SupportedDatabase provider)
    {
        registry.Register(provider, new ListOfIntCoercion());
        registry.Register(provider, new JsonObjectCoercion());
        registry.Register(provider, new RangeArrayCoercion());
    }
}

/// <summary>
/// A float vector read from its bracketed text ("[1,2.5,3]"), the form TiDB's VECTOR returns. Text that is not a vector
/// declines.
/// </summary>
internal sealed class BracketedVectorCoercion : DbCoercion<float[]>
{
    public override bool TryRead(in DbValue src, out float[] value)
    {
        if (src.IsNull || src.RawValue is not string text)
        {
            value = null!;
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']')
        {
            value = null!;
            return false;
        }

        var parts = trimmed[1..^1].Split(',', System.StringSplitOptions.RemoveEmptyEntries);
        var vector = new float[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out vector[i]))
            {
                value = null!;
                return false;
            }
        }

        value = vector;
        return true;
    }
}
