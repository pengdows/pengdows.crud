#region

using System.Buffers.Binary;

#endregion

namespace pengdows.crud.fakeDb;

/// <summary>
/// SQL Server's stored geometry or geography, built as geometry::STGeomFromWKB(bytes, srid) builds it: the bound argument
/// is a big-endian SRID then the ISO WKB. The native version-1 encoding is the SRID (int32, little-endian), the version
/// (1), the flags, then the points as doubles. A single point stores flag 0x08 and no more; a line or polygon stores its
/// point count and points, then its figures (attribute and first point index) and its shapes (parent, figure, type). A
/// geography stores each point's latitude before its longitude.
/// </summary>
internal static class SqlServerSpatialWire
{
    private const byte SinglePoint = 0x08;
    private const byte PointType = 1;
    private const byte LineStringType = 2;
    private const byte PolygonType = 3;

    /// <summary>The native value for a bound argument (big-endian SRID, then WKB); null stays null.</summary>
    public static byte[]? Encode(object? argument, bool geography)
    {
        if (argument is null)
        {
            return null;
        }

        var bytes = (byte[])argument;
        var srid = BinaryPrimitives.ReadInt32BigEndian(bytes);
        var reader = new WkbReader(bytes.AsSpan(4));
        var native = new List<byte>();
        AppendInt(native, srid);
        native.Add(1);

        switch (reader.Type)
        {
            case PointType:
                native.Add(SinglePoint);
                AppendPoint(native, reader.Point(), geography);
                break;
            case LineStringType:
                native.Add(0);
                var line = reader.Points();
                AppendInt(native, line.Count);
                foreach (var point in line)
                {
                    AppendPoint(native, point, geography);
                }

                AppendInt(native, 1);
                native.Add(1);
                AppendInt(native, 0);
                AppendInt(native, 1);
                AppendInt(native, -1);
                AppendInt(native, 0);
                native.Add(LineStringType);
                break;
            case PolygonType:
                native.Add(0);
                var rings = reader.Rings();
                var points = rings.SelectMany(r => r).ToList();
                AppendInt(native, points.Count);
                foreach (var point in points)
                {
                    AppendPoint(native, point, geography);
                }

                AppendInt(native, rings.Count);
                var offset = 0;
                for (var i = 0; i < rings.Count; i++)
                {
                    native.Add((byte)(i == 0 ? 1 : 0));
                    AppendInt(native, offset);
                    offset += rings[i].Count;
                }

                AppendInt(native, 1);
                AppendInt(native, -1);
                AppendInt(native, 0);
                native.Add(PolygonType);
                break;
            default:
                throw new NotSupportedException($"FakeDataStore cannot store a SQL Server spatial value of WKB type {reader.Type}.");
        }

        return native.ToArray();
    }

    private static void AppendPoint(List<byte> output, (double X, double Y) point, bool geography)
    {
        var (first, second) = geography ? (point.Y, point.X) : (point.X, point.Y);
        Span<byte> buffer = stackalloc byte[16];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, first);
        BinaryPrimitives.WriteDoubleLittleEndian(buffer.Slice(8), second);
        output.AddRange(buffer.ToArray());
    }

    private static void AppendInt(List<byte> output, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        output.AddRange(buffer.ToArray());
    }

    // The ISO WKB of a point, line or polygon, in either byte order.
    private sealed class WkbReader
    {
        private readonly byte[] _data;
        private readonly bool _littleEndian;
        private int _at;

        public WkbReader(ReadOnlySpan<byte> wkb)
        {
            _data = wkb.ToArray();
            _littleEndian = _data[0] == 1;
            _at = 1;
            Type = (int)Int32();
        }

        public int Type { get; }

        public (double X, double Y) Point()
        {
            var x = Double();
            var y = Double();
            return (x, y);
        }

        public List<(double X, double Y)> Points()
        {
            var count = Int32();
            var points = new List<(double X, double Y)>(count);
            for (var i = 0; i < count; i++)
            {
                points.Add(Point());
            }

            return points;
        }

        public List<List<(double X, double Y)>> Rings()
        {
            var count = Int32();
            var rings = new List<List<(double X, double Y)>>(count);
            for (var i = 0; i < count; i++)
            {
                rings.Add(Points());
            }

            return rings;
        }

        private int Int32()
        {
            var value = _littleEndian
                ? BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_at))
                : BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(_at));
            _at += 4;
            return value;
        }

        private double Double()
        {
            var value = _littleEndian
                ? BinaryPrimitives.ReadDoubleLittleEndian(_data.AsSpan(_at))
                : BinaryPrimitives.ReadDoubleBigEndian(_data.AsSpan(_at));
            _at += 8;
            return value;
        }
    }
}
