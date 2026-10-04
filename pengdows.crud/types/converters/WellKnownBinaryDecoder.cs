// =============================================================================
// FILE: WellKnownBinaryDecoder.cs
// PURPOSE: Decodes 2D OGC well-known binary as well-known text; the inverse of WellKnownTextEncoder.
//
// AI SUMMARY:
// - ToWellKnownText(): POINT, LINESTRING, POLYGON, MULTIPOINT, MULTILINESTRING, MULTIPOLYGON and
//   GEOMETRYCOLLECTION, including EMPTY; either byte order; an EWKB SRID is skipped.
// - Coordinates are written round-trip ("R"), so no double changes on the way through text.
// - Z/M coordinates (ISO 1000+ codes or EWKB flags) are refused rather than silently dropped.
// - Used where a database builds geometries only from text (Oracle SDO_GEOMETRY, TYPE-021).
// =============================================================================

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace pengdows.crud.types.converters;

/// <summary>
/// Decodes 2D well-known binary (either byte order) as well-known text.
/// </summary>
internal static class WellKnownBinaryDecoder
{
    private const uint EwkbZ = 0x80000000;
    private const uint EwkbM = 0x40000000;
    private const uint EwkbSrid = 0x20000000;

    public static string ToWellKnownText(ReadOnlySpan<byte> wkb)
    {
        var position = 0;
        var text = new StringBuilder();
        WriteGeometry(wkb, ref position, text);
        if (position != wkb.Length)
        {
            throw new FormatException("Unexpected bytes after the geometry in WKB.");
        }

        return text.ToString();
    }

    private static void WriteGeometry(ReadOnlySpan<byte> wkb, ref int position, StringBuilder text)
    {
        var littleEndian = ReadByte(wkb, ref position) switch
        {
            1 => true,
            0 => false,
            var order => throw new FormatException($"Unknown WKB byte order {order}.")
        };

        var rawType = ReadUInt32(wkb, ref position, littleEndian);
        if (((rawType & EwkbZ) != 0) || ((rawType & EwkbM) != 0) || ((rawType & 0x0FFFFFFF) >= 1000))
        {
            throw new NotSupportedException("WKB with Z or M coordinates is not supported; only 2D geometries are decoded.");
        }

        if ((rawType & EwkbSrid) != 0)
        {
            ReadUInt32(wkb, ref position, littleEndian);
        }

        var type = rawType & 0x0FFFFFFF;
        text.Append(type switch
        {
            1 => "POINT",
            2 => "LINESTRING",
            3 => "POLYGON",
            4 => "MULTIPOINT",
            5 => "MULTILINESTRING",
            6 => "MULTIPOLYGON",
            7 => "GEOMETRYCOLLECTION",
            _ => throw new FormatException($"Unknown WKB geometry type {type}.")
        });

        if (type == 1)
        {
            var x = ReadDouble(wkb, ref position, littleEndian);
            var y = ReadDouble(wkb, ref position, littleEndian);
            if (double.IsNaN(x) && double.IsNaN(y))
            {
                text.Append(" EMPTY");
                return;
            }

            text.Append(" (");
            AppendCoordinate(text, x, y);
            text.Append(')');
            return;
        }

        var count = ReadUInt32(wkb, ref position, littleEndian);
        if (count == 0)
        {
            text.Append(" EMPTY");
            return;
        }

        text.Append(" (");
        for (var i = 0u; i < count; i++)
        {
            if (i > 0)
            {
                text.Append(", ");
            }

            switch (type)
            {
                case 2:
                    AppendCoordinate(text, ReadDouble(wkb, ref position, littleEndian), ReadDouble(wkb, ref position, littleEndian));
                    break;
                case 3:
                    AppendPointList(wkb, ref position, littleEndian, text);
                    break;
                default:
                    AppendMember(wkb, ref position, text, type);
                    break;
            }
        }

        text.Append(')');
    }

    // A member of a MULTI* geometry is a full geometry whose text omits its own type name.
    private static void AppendMember(ReadOnlySpan<byte> wkb, ref int position, StringBuilder text, uint collectionType)
    {
        if (collectionType == 7)
        {
            WriteGeometry(wkb, ref position, text);
            return;
        }

        var member = new StringBuilder();
        WriteGeometry(wkb, ref position, member);
        var body = member.ToString();
        var open = body.IndexOf('(');
        if (open < 0)
        {
            throw new FormatException("An empty member in a MULTI geometry can't be written as WKT.");
        }

        text.Append(body, open, body.Length - open);
    }

    private static void AppendPointList(ReadOnlySpan<byte> wkb, ref int position, bool littleEndian, StringBuilder text)
    {
        var points = ReadUInt32(wkb, ref position, littleEndian);
        text.Append('(');
        for (var i = 0u; i < points; i++)
        {
            if (i > 0)
            {
                text.Append(", ");
            }

            AppendCoordinate(text, ReadDouble(wkb, ref position, littleEndian), ReadDouble(wkb, ref position, littleEndian));
        }

        text.Append(')');
    }

    private static void AppendCoordinate(StringBuilder text, double x, double y)
    {
        text.Append(x.ToString("R", CultureInfo.InvariantCulture));
        text.Append(' ');
        text.Append(y.ToString("R", CultureInfo.InvariantCulture));
    }

    private static byte ReadByte(ReadOnlySpan<byte> wkb, ref int position)
    {
        if (position >= wkb.Length)
        {
            throw new FormatException("WKB ended before the geometry did.");
        }

        return wkb[position++];
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> wkb, ref int position, bool littleEndian)
    {
        var bytes = Take(wkb, ref position, 4);
        return littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static double ReadDouble(ReadOnlySpan<byte> wkb, ref int position, bool littleEndian)
    {
        var bytes = Take(wkb, ref position, 8);
        return littleEndian ? BinaryPrimitives.ReadDoubleLittleEndian(bytes) : BinaryPrimitives.ReadDoubleBigEndian(bytes);
    }

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> wkb, ref int position, int length)
    {
        if (position + length > wkb.Length)
        {
            throw new FormatException("WKB ended before the geometry did.");
        }

        var bytes = wkb.Slice(position, length);
        position += length;
        return bytes;
    }
}
