// =============================================================================
// FILE: BsonJson.cs
// PURPOSE: The JSON text of a BSON document (the binary JSON a SingleStore BSON column returns).
//
// AI SUMMARY:
// - A BSON document is an int32 length (little-endian), its elements, then a zero byte. An element is a type byte, a
//   zero-terminated name, and the value: double (0x01), string (0x02), embedded document (0x03), array (0x04, a document
//   whose names are indexes), bool (0x08), null (0x0A), int32 (0x10), int64 (0x12).
// - ToJson() writes that document as JSON text with Utf8JsonWriter (so names and strings are escaped correctly). A type
//   outside that set is refused (NotSupportedException): the value cannot be read without its type's own decoder.
// =============================================================================

using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace pengdows.crud.types.converters;

internal static class BsonJson
{
    /// <summary>Whether the bytes are a BSON document: their first int32 is their own length.</summary>
    public static bool IsDocument(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 5 && BinaryPrimitives.ReadInt32LittleEndian(bytes) == bytes.Length;

    /// <summary>The JSON text of the BSON document <paramref name="bson"/>.</summary>
    public static string ToJson(ReadOnlySpan<byte> bson)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            var at = 0;
            WriteDocument(writer, bson, ref at, asArray: false);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteDocument(Utf8JsonWriter writer, ReadOnlySpan<byte> data, ref int at, bool asArray)
    {
        var start = at;
        var length = ReadInt32(data, ref at);
        if (length < 5 || start + length > data.Length)
        {
            throw new FormatException("The value is not a BSON document.");
        }

        var end = start + length;
        if (asArray)
        {
            writer.WriteStartArray();
        }
        else
        {
            writer.WriteStartObject();
        }

        while (data[at] != 0)
        {
            var type = data[at++];
            var name = ReadCString(data, ref at);
            if (!asArray)
            {
                writer.WritePropertyName(name);
            }

            WriteValue(writer, data, ref at, type);
        }

        at++; // the document's terminator
        if (at != end)
        {
            throw new FormatException("The BSON document's length does not match its elements.");
        }

        if (asArray)
        {
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteEndObject();
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, ReadOnlySpan<byte> data, ref int at, byte type)
    {
        switch (type)
        {
            case 0x01:
                writer.WriteNumberValue(BinaryPrimitives.ReadDoubleLittleEndian(data.Slice(at, 8)));
                at += 8;
                break;
            case 0x02:
                var length = ReadInt32(data, ref at);
                writer.WriteStringValue(Encoding.UTF8.GetString(data.Slice(at, length - 1)));
                at += length;
                break;
            case 0x03:
                WriteDocument(writer, data, ref at, asArray: false);
                break;
            case 0x04:
                WriteDocument(writer, data, ref at, asArray: true);
                break;
            case 0x08:
                writer.WriteBooleanValue(data[at++] != 0);
                break;
            case 0x0A:
                writer.WriteNullValue();
                break;
            case 0x10:
                writer.WriteNumberValue(ReadInt32(data, ref at));
                break;
            case 0x12:
                writer.WriteNumberValue(BinaryPrimitives.ReadInt64LittleEndian(data.Slice(at, 8)));
                at += 8;
                break;
            default:
                throw new NotSupportedException($"A BSON element of type 0x{type:X2} has no JSON form in this reader.");
        }
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int at)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(at, 4));
        at += 4;
        return value;
    }

    private static string ReadCString(ReadOnlySpan<byte> data, ref int at)
    {
        var end = data.Slice(at).IndexOf((byte)0);
        if (end < 0)
        {
            throw new FormatException("A BSON element name is not terminated.");
        }

        var name = Encoding.UTF8.GetString(data.Slice(at, end));
        at += end + 1;
        return name;
    }
}
