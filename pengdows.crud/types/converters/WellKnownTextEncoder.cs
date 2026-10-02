// =============================================================================
// FILE: WellKnownTextEncoder.cs
// PURPOSE: Encodes 2D OGC well-known text as little-endian ISO well-known binary.
//
// AI SUMMARY:
// - Encode(): POINT, LINESTRING, POLYGON, MULTIPOINT, MULTILINESTRING, MULTIPOLYGON and
//   GEOMETRYCOLLECTION, including EMPTY; an "SRID=n;" prefix is ignored.
// - Z/M coordinates are refused (NotSupportedException) rather than silently dropped.
// - Used where a database needs WKB but the value only carries WKT (MySQL's internal format).
// =============================================================================

using System.Buffers.Binary;
using System.Globalization;

namespace pengdows.crud.types.converters;

/// <summary>
/// Encodes 2D well-known text as little-endian ISO well-known binary.
/// </summary>
internal static class WellKnownTextEncoder
{
    private const uint Point = 1;
    private const uint LineString = 2;
    private const uint Polygon = 3;
    private const uint MultiPoint = 4;
    private const uint MultiLineString = 5;
    private const uint MultiPolygon = 6;
    private const uint GeometryCollection = 7;

    public static byte[] Encode(string wkt)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        var text = wkt.Trim();
        if (text.StartsWith("SRID=", StringComparison.OrdinalIgnoreCase))
        {
            var separator = text.IndexOf(';');
            text = separator < 0 ? string.Empty : text[(separator + 1)..];
        }

        var reader = new Reader(text);
        var output = new List<byte>(64);
        WriteGeometry(ref reader, output);
        reader.SkipWhitespace();
        if (!reader.AtEnd)
        {
            throw new FormatException($"Unexpected text after the geometry in WKT: '{wkt}'.");
        }

        return output.ToArray();
    }

    private static void WriteGeometry(ref Reader reader, List<byte> output)
    {
        var tag = reader.ReadWord().ToUpperInvariant();
        var dimension = reader.PeekWord().ToUpperInvariant();
        if (dimension is "Z" or "M" or "ZM")
        {
            throw new NotSupportedException($"WKT with {dimension} coordinates is not supported; only 2D geometries are encoded.");
        }

        var type = tag switch
        {
            "POINT" => Point,
            "LINESTRING" => LineString,
            "POLYGON" => Polygon,
            "MULTIPOINT" => MultiPoint,
            "MULTILINESTRING" => MultiLineString,
            "MULTIPOLYGON" => MultiPolygon,
            "GEOMETRYCOLLECTION" => GeometryCollection,
            _ => throw new FormatException($"Unknown WKT geometry type '{tag}'.")
        };

        WriteHeader(output, type);
        var empty = reader.TryReadEmpty();

        switch (type)
        {
            case Point:
                if (empty)
                {
                    WriteDouble(output, double.NaN);
                    WriteDouble(output, double.NaN);
                    return;
                }

                reader.Expect('(');
                WriteCoordinate(ref reader, output);
                reader.Expect(')');
                return;
            case LineString:
                WritePointList(ref reader, output, empty);
                return;
            case Polygon:
                WriteRings(ref reader, output, empty);
                return;
            case MultiPoint:
                WriteCollection(ref reader, output, empty, (ref Reader r, List<byte> o) =>
                {
                    // MULTIPOINT((1 2), (3 4)) and the older MULTIPOINT(1 2, 3 4) are both valid.
                    WriteHeader(o, Point);
                    var parenthesized = r.TryRead('(');
                    WriteCoordinate(ref r, o);
                    if (parenthesized)
                    {
                        r.Expect(')');
                    }
                });
                return;
            case MultiLineString:
                WriteCollection(ref reader, output, empty, (ref Reader r, List<byte> o) =>
                {
                    WriteHeader(o, LineString);
                    WritePointList(ref r, o, false);
                });
                return;
            case MultiPolygon:
                WriteCollection(ref reader, output, empty, (ref Reader r, List<byte> o) =>
                {
                    WriteHeader(o, Polygon);
                    WriteRings(ref r, o, false);
                });
                return;
            default:
                WriteCollection(ref reader, output, empty, WriteGeometry);
                return;
        }
    }

    private delegate void ElementWriter(ref Reader reader, List<byte> output);

    private static void WriteCollection(ref Reader reader, List<byte> output, bool empty, ElementWriter element)
    {
        var countAt = output.Count;
        WriteUInt32(output, 0);
        if (empty)
        {
            return;
        }

        reader.Expect('(');
        uint count = 0;
        do
        {
            element(ref reader, output);
            count++;
        } while (reader.TryRead(','));

        reader.Expect(')');
        PatchUInt32(output, countAt, count);
    }

    private static void WriteRings(ref Reader reader, List<byte> output, bool empty)
    {
        var countAt = output.Count;
        WriteUInt32(output, 0);
        if (empty)
        {
            return;
        }

        reader.Expect('(');
        uint rings = 0;
        do
        {
            WritePointList(ref reader, output, false);
            rings++;
        } while (reader.TryRead(','));

        reader.Expect(')');
        PatchUInt32(output, countAt, rings);
    }

    private static void WritePointList(ref Reader reader, List<byte> output, bool empty)
    {
        var countAt = output.Count;
        WriteUInt32(output, 0);
        if (empty)
        {
            return;
        }

        reader.Expect('(');
        uint points = 0;
        do
        {
            WriteCoordinate(ref reader, output);
            points++;
        } while (reader.TryRead(','));

        reader.Expect(')');
        PatchUInt32(output, countAt, points);
    }

    private static void WriteCoordinate(ref Reader reader, List<byte> output)
    {
        WriteDouble(output, reader.ReadNumber());
        WriteDouble(output, reader.ReadNumber());
        reader.SkipWhitespace();
        if (!reader.AtEnd && reader.Current is not (',' or ')'))
        {
            throw new NotSupportedException("WKT with more than two coordinates per point is not supported; only 2D geometries are encoded.");
        }
    }

    private static void WriteHeader(List<byte> output, uint type)
    {
        output.Add(1); // little endian
        WriteUInt32(output, type);
    }

    private static void WriteUInt32(List<byte> output, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        foreach (var b in buffer)
        {
            output.Add(b);
        }
    }

    private static void PatchUInt32(List<byte> output, int index, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        for (var i = 0; i < 4; i++)
        {
            output[index + i] = buffer[i];
        }
    }

    private static void WriteDouble(List<byte> output, double value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        foreach (var b in buffer)
        {
            output.Add(b);
        }
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<char> _text;
        private int _position;

        public Reader(string text)
        {
            _text = text.AsSpan();
            _position = 0;
        }

        public bool AtEnd => _position >= _text.Length;
        public char Current => _text[_position];

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }

        public string ReadWord()
        {
            SkipWhitespace();
            var start = _position;
            while (!AtEnd && char.IsLetter(_text[_position]))
            {
                _position++;
            }

            if (_position == start)
            {
                throw new FormatException("Expected a WKT keyword.");
            }

            return _text[start.._position].ToString();
        }

        public string PeekWord()
        {
            var saved = _position;
            SkipWhitespace();
            var start = _position;
            while (!AtEnd && char.IsLetter(_text[_position]))
            {
                _position++;
            }

            var word = _text[start.._position].ToString();
            if (word.Equals("Z", StringComparison.OrdinalIgnoreCase) ||
                word.Equals("M", StringComparison.OrdinalIgnoreCase) ||
                word.Equals("ZM", StringComparison.OrdinalIgnoreCase))
            {
                return word;
            }

            _position = saved;
            return word;
        }

        public bool TryReadEmpty()
        {
            var saved = _position;
            SkipWhitespace();
            var start = _position;
            while (!AtEnd && char.IsLetter(_text[_position]))
            {
                _position++;
            }

            if (_text[start.._position].Equals("EMPTY", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            _position = saved;
            return false;
        }

        public bool TryRead(char expected)
        {
            SkipWhitespace();
            if (!AtEnd && _text[_position] == expected)
            {
                _position++;
                return true;
            }

            return false;
        }

        public void Expect(char expected)
        {
            if (!TryRead(expected))
            {
                throw new FormatException($"Expected '{expected}' in WKT at position {_position}.");
            }
        }

        public double ReadNumber()
        {
            SkipWhitespace();
            var start = _position;
            while (!AtEnd && (char.IsDigit(_text[_position]) || _text[_position] is '-' or '+' or '.' or 'e' or 'E'))
            {
                _position++;
            }

            if (_position == start ||
                !double.TryParse(_text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"Expected a number in WKT at position {start}.");
            }

            return value;
        }
    }
}
