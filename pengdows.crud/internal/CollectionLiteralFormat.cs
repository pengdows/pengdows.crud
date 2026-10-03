// =============================================================================
// FILE: CollectionLiteralFormat.cs
// PURPOSE: Informix collection literals ("LIST{1,-2}", "SET{'a','it''s'}") to and from arrays.
//
// AI SUMMARY:
// - Informix.Net.Core reads LIST/SET/MULTISET as their literal text (numbers space-padded, strings
//   single-quoted with '' escapes) and Informix converts a literal text parameter to the column,
//   a LIST literal into a SET or MULTISET column too (confirmed live, TYPE-002).
// - Format(): an array as a LIST literal (InformixDialect writes it).
// - Parse<T>(): a literal into T[]; counts the elements first so only the result array is allocated,
//   and parses numbers from spans (the generic branches are resolved per T by the JIT). Anything
//   malformed or unconvertible throws, so hydration fails loudly (DataMappingException).
// =============================================================================

using System.Globalization;
using System.Text;

namespace pengdows.crud.@internal;

internal static class CollectionLiteralFormat
{
    public static string Format(Array values)
    {
        var builder = new StringBuilder("LIST{");
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            AppendElement(builder, values.GetValue(i));
        }

        return builder.Append('}').ToString();
    }

    private static void AppendElement(StringBuilder builder, object? element)
    {
        switch (element)
        {
            case null:
                throw new ArgumentException("An Informix collection element can't be NULL.");
            case string text:
                builder.Append('\'').Append(text.Replace("'", "''")).Append('\'');
                break;
            case char character:
                builder.Append('\'').Append(character == '\'' ? "''" : character.ToString()).Append('\'');
                break;
            case bool flag:
                builder.Append(flag ? "'t'" : "'f'");
                break;
            case IFormattable formattable when IsNumeric(element):
                builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                break;
            case IFormattable formattable:
                builder.Append('\'').Append(formattable.ToString(null, CultureInfo.InvariantCulture)!.Replace("'", "''")).Append('\'');
                break;
            default:
                builder.Append('\'').Append(Convert.ToString(element, CultureInfo.InvariantCulture)!.Replace("'", "''")).Append('\'');
                break;
        }
    }

    private static bool IsNumeric(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    public static T[] Parse<T>(string text)
    {
        var open = text.IndexOf('{');
        if (open <= 0 || text[^1] != '}' || !IsKeyword(text.AsSpan(0, open)))
        {
            throw new FormatException($"Not an Informix collection literal: '{text}'.");
        }

        var body = text.AsSpan(open + 1, text.Length - open - 2);
        var result = new T[Count(body)];
        var position = 0;
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ParseElement<T>(body, ref position);
        }

        return result;
    }

    private static bool IsKeyword(ReadOnlySpan<char> keyword) =>
        keyword.Equals("LIST", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("SET", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("MULTISET", StringComparison.OrdinalIgnoreCase);

    private static int Count(ReadOnlySpan<char> body)
    {
        if (body.Trim().IsEmpty)
        {
            return 0;
        }

        var count = 1;
        var quoted = false;
        foreach (var c in body)
        {
            if (c == '\'')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                count++;
            }
        }

        if (quoted)
        {
            throw new FormatException("Unterminated string in an Informix collection literal.");
        }

        return count;
    }

    private static T ParseElement<T>(ReadOnlySpan<char> body, ref int position)
    {
        while (position < body.Length && body[position] == ' ')
        {
            position++;
        }

        if (position < body.Length && body[position] == '\'')
        {
            var text = ReadQuoted(body, ref position);
            SkipSeparator(body, ref position);
            return ConvertElement<T>(text.AsSpan(), text);
        }

        var start = position;
        while (position < body.Length && body[position] != ',')
        {
            position++;
        }

        var token = body[start..position].Trim();
        SkipSeparator(body, ref position);
        return ConvertElement<T>(token, null);
    }

    private static string ReadQuoted(ReadOnlySpan<char> body, ref int position)
    {
        var builder = new StringBuilder();
        position++;
        while (position < body.Length)
        {
            var c = body[position++];
            if (c != '\'')
            {
                builder.Append(c);
                continue;
            }

            if (position < body.Length && body[position] == '\'')
            {
                builder.Append('\'');
                position++;
                continue;
            }

            return builder.ToString();
        }

        throw new FormatException("Unterminated string in an Informix collection literal.");
    }

    private static void SkipSeparator(ReadOnlySpan<char> body, ref int position)
    {
        while (position < body.Length && body[position] == ' ')
        {
            position++;
        }

        if (position < body.Length && body[position] == ',')
        {
            position++;
        }
    }

    private static T ConvertElement<T>(ReadOnlySpan<char> token, string? quoted)
    {
        var invariant = CultureInfo.InvariantCulture;
        if (typeof(T) == typeof(string))
        {
            return (T)(object)(quoted ?? token.ToString());
        }

        if (typeof(T) == typeof(int))
        {
            return (T)(object)int.Parse(token, NumberStyles.Integer, invariant);
        }

        if (typeof(T) == typeof(long))
        {
            return (T)(object)long.Parse(token, NumberStyles.Integer, invariant);
        }

        if (typeof(T) == typeof(short))
        {
            return (T)(object)short.Parse(token, NumberStyles.Integer, invariant);
        }

        if (typeof(T) == typeof(decimal))
        {
            return (T)(object)decimal.Parse(token, NumberStyles.Float, invariant);
        }

        if (typeof(T) == typeof(double))
        {
            return (T)(object)double.Parse(token, NumberStyles.Float, invariant);
        }

        if (typeof(T) == typeof(float))
        {
            return (T)(object)float.Parse(token, NumberStyles.Float, invariant);
        }

        var text = quoted ?? token.ToString();
        return (T)TypeCoercionHelper.Coerce(text, typeof(string), typeof(T))!;
    }
}
