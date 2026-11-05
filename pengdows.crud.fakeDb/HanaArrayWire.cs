#region

using System.Buffers.Binary;
using System.Text.Json;
using System.Text.RegularExpressions;

#endregion

namespace pengdows.crud.fakeDb;

/// <summary>
/// The SAP HANA ARRAY a server-side ARRAY(SELECT V FROM JSON_TABLE(...)) builds, as the driver returns it: a varbinary in
/// HANA's wire encoding. An int32 element count, then each element. INTEGER, BIGINT and SMALLINT: a 00 (NULL, no value
/// follows) or 01 indicator and the little-endian value. DOUBLE and REAL: the raw little-endian value, all 0xFF for NULL.
/// (N)VARCHAR: a length (one byte up to 245, F6 + uint16, or F7 + int32) then the CESU-8 bytes, FF for NULL.
/// </summary>
internal static class HanaArrayWire
{
    private static readonly Regex ElementType = new(
        @"V\s+(NVARCHAR|VARCHAR|INTEGER|INT|BIGINT|SMALLINT|DOUBLE|REAL)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CastElementType = new(
        @"CAST\(\s*V\s+AS\s+(NVARCHAR|VARCHAR|INTEGER|INT|BIGINT|SMALLINT|DOUBLE|REAL)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The element SQL type the expression names: its cast, else the JSON_TABLE COLUMNS type (V &lt;type&gt; PATH '$').</summary>
    public static string ElementTypeOf(string expression)
    {
        // A cast in the subquery names the array's element type; JSON_TABLE's own column type is the source's.
        var cast = CastElementType.Match(expression);
        if (cast.Success)
        {
            return cast.Groups[1].Value.ToUpperInvariant();
        }

        var match = ElementType.Match(expression);
        if (!match.Success)
        {
            throw new NotSupportedException($"FakeDataStore cannot emulate this SAP HANA ARRAY expression: '{expression}'.");
        }

        return match.Groups[1].Value.ToUpperInvariant();
    }

    /// <summary>The varbinary a HANA ARRAY holds for the bound JSON array (null stays null).</summary>
    public static byte[]? Encode(object? json, string elementType)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json.ToString() ?? string.Empty);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("A SAP HANA ARRAY is built from a JSON array.");
        }

        var elements = document.RootElement.EnumerateArray().ToList();
        var output = new List<byte>();
        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(count, elements.Count);
        output.AddRange(count.ToArray());

        foreach (var element in elements)
        {
            switch (elementType)
            {
                case "INTEGER" or "INT":
                    WriteIndicated(output, element, 4, (e, buffer) => BinaryPrimitives.WriteInt32LittleEndian(buffer, checked((int)e.GetInt64())));
                    break;
                case "SMALLINT":
                    WriteIndicated(output, element, 2, (e, buffer) => BinaryPrimitives.WriteInt16LittleEndian(buffer, checked((short)e.GetInt64())));
                    break;
                case "BIGINT":
                    WriteIndicated(output, element, 8, (e, buffer) => BinaryPrimitives.WriteInt64LittleEndian(buffer, e.GetInt64()));
                    break;
                case "DOUBLE":
                    WriteFloating(output, element, 8, (e, buffer) => BinaryPrimitives.WriteDoubleLittleEndian(buffer, e.GetDouble()));
                    break;
                case "REAL":
                    WriteFloating(output, element, 4, (e, buffer) => BinaryPrimitives.WriteSingleLittleEndian(buffer, (float)e.GetDouble()));
                    break;
                case "VARCHAR" or "NVARCHAR":
                    WriteString(output, element);
                    break;
                default:
                    throw new NotSupportedException($"FakeDataStore cannot emulate a SAP HANA ARRAY of {elementType}.");
            }
        }

        return output.ToArray();
    }

    private static void WriteIndicated(List<byte> output, JsonElement element, int width, Action<JsonElement, byte[]> write)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            output.Add(0);
            return;
        }

        output.Add(1);
        var buffer = new byte[width];
        write(element, buffer);
        output.AddRange(buffer);
    }

    private static void WriteFloating(List<byte> output, JsonElement element, int width, Action<JsonElement, byte[]> write)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            output.AddRange(Enumerable.Repeat((byte)0xFF, width));
            return;
        }

        var buffer = new byte[width];
        write(element, buffer);
        output.AddRange(buffer);
    }

    private static void WriteString(List<byte> output, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            output.Add(0xFF);
            return;
        }

        var cesu = Cesu8Bytes(element.GetString() ?? string.Empty);
        if (cesu.Count <= 245)
        {
            output.Add((byte)cesu.Count);
        }
        else if (cesu.Count <= 0xFFFF)
        {
            output.Add(0xF6);
            output.Add((byte)(cesu.Count & 0xFF));
            output.Add((byte)(cesu.Count >> 8));
        }
        else
        {
            output.Add(0xF7);
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, cesu.Count);
            output.AddRange(length.ToArray());
        }

        output.AddRange(cesu);
    }

    // CESU-8: each UTF-16 code unit as UTF-8 (a surrogate as its own three bytes, not the four-byte UTF-8 sequence).
    private static List<byte> Cesu8Bytes(string text)
    {
        var bytes = new List<byte>(text.Length);
        foreach (var unit in text)
        {
            var c = (int)unit;
            if (c < 0x80)
            {
                bytes.Add((byte)c);
            }
            else if (c < 0x800)
            {
                bytes.Add((byte)(0xC0 | (c >> 6)));
                bytes.Add((byte)(0x80 | (c & 0x3F)));
            }
            else
            {
                bytes.Add((byte)(0xE0 | (c >> 12)));
                bytes.Add((byte)(0x80 | ((c >> 6) & 0x3F)));
                bytes.Add((byte)(0x80 | (c & 0x3F)));
            }
        }

        return bytes;
    }
}
