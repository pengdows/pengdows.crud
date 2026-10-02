// =============================================================================
// FILE: SqlServerSpatialFormat.cs
// PURPOSE: SQL Server geometry/geography without Microsoft.SqlServer.Types.
//
// AI SUMMARY:
// - Decode(): SQL Server's stored encoding ([MS-SSCLRT] serialization versions 1 and 2) to SRID +
//   ISO WKB (Z/M kept as ISO 1000/2000/3000 types). Geography stores latitude first; WKB gets
//   longitude (x) first. Curves (arcs, CIRCULARSTRING/COMPOUNDCURVE/CURVEPOLYGON) and FULLGLOBE
//   have no WKB form and throw NotSupportedException.
// - ToConstructorArgument(): the bytes a gateway binds for a spatial column, a big-endian SRID
//   followed by the value's WKB. SqlServerDialect renders the column's value as
//   geometry::STGeomFromWKB(SUBSTRING(@p, 5, ...), CAST(SUBSTRING(@p, 1, 4) AS int)) (or
//   geography::), so the server builds and validates the instance and keeps the SRID.
// - The stored encoding is never written: SQL Server trusts its validity flag, so a client-built
//   instance with a wrong flag gives wrong results (an invalid bowtie reported area 0) or refuses
//   every operation (confirmed live on SQL Server 2025).
// =============================================================================

using System.Buffers.Binary;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Reads SQL Server's stored geometry/geography encoding and builds the SRID-prefixed WKB a
/// spatial column's parameter carries.
/// </summary>
internal static class SqlServerSpatialFormat
{
    private const byte HasZ = 0x01;
    private const byte HasM = 0x02;
    private const byte SinglePoint = 0x08;
    private const byte SingleLineSegment = 0x10;
    private const byte FullGlobe = 0x20;

    private const byte Point = 1;
    private const byte LineString = 2;
    private const byte Polygon = 3;
    private const byte GeometryCollection = 7;

    /// <summary>
    /// Builds the parameter value for a spatial column: a big-endian SRID, then the value's WKB.
    /// </summary>
    public static byte[] ToConstructorArgument(SpatialValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] wkb;
        if (!value.WellKnownBinary.IsEmpty)
        {
            GeometryConverter.ExtractSridFromEwkb(value.WellKnownBinary.Span, out _, out wkb);
        }
        else if (!string.IsNullOrEmpty(value.WellKnownText))
        {
            wkb = WellKnownTextEncoder.Encode(value.WellKnownText);
        }
        else
        {
            throw new NotSupportedException(
                "SQL Server spatial values need WKB or WKT; a GeoJSON-only value cannot be written. " +
                "Create it with FromWellKnownText or FromWellKnownBinary.");
        }

        var argument = new byte[4 + wkb.Length];
        BinaryPrimitives.WriteInt32BigEndian(argument, value.Srid);
        wkb.CopyTo(argument, 4);
        return argument;
    }

    /// <summary>
    /// Decodes SQL Server's stored geometry (or, with <paramref name="geography"/>, geography)
    /// encoding to its SRID and ISO WKB.
    /// </summary>
    public static (int Srid, byte[] Wkb) Decode(ReadOnlySpan<byte> data, bool geography)
    {
        var reader = new Reader(data);
        var srid = reader.Int32();
        var version = reader.Byte();
        if (version is not (1 or 2))
        {
            throw new NotSupportedException($"SQL Server spatial serialization version {version} is not supported.");
        }

        var flags = reader.Byte();
        if ((flags & FullGlobe) != 0)
        {
            throw new NotSupportedException("FULLGLOBE has no well-known binary form.");
        }

        int pointCount;
        if ((flags & SinglePoint) != 0)
        {
            pointCount = 1;
        }
        else if ((flags & SingleLineSegment) != 0)
        {
            pointCount = 2;
        }
        else
        {
            pointCount = reader.Count();
        }

        var x = new double[pointCount];
        var y = new double[pointCount];
        for (var i = 0; i < pointCount; i++)
        {
            var first = reader.Double();
            var second = reader.Double();
            // Geography stores latitude, longitude; WKB is x = longitude, y = latitude.
            (x[i], y[i]) = geography ? (second, first) : (first, second);
        }

        var z = (flags & HasZ) != 0 ? reader.Doubles(pointCount) : null;
        var m = (flags & HasM) != 0 ? reader.Doubles(pointCount) : null;

        int[] figureOffsets;
        Shape[] shapes;
        if ((flags & (SinglePoint | SingleLineSegment)) != 0)
        {
            figureOffsets = new[] { 0 };
            shapes = new[] { new Shape(-1, 0, (flags & SinglePoint) != 0 ? Point : LineString) };
        }
        else
        {
            var figureCount = reader.Count();
            figureOffsets = new int[figureCount];
            for (var i = 0; i < figureCount; i++)
            {
                var attribute = reader.Byte();
                // Version 2 marks arcs (2) and composite curves (3); version 1 uses 0-2 for rings
                // and strokes, all straight-line.
                if (version == 2 && attribute is 2 or 3)
                {
                    throw new NotSupportedException("SQL Server curve segments have no well-known binary form.");
                }

                figureOffsets[i] = reader.Int32();
            }

            var shapeCount = reader.Count();
            shapes = new Shape[shapeCount];
            for (var i = 0; i < shapeCount; i++)
            {
                var parent = reader.Int32();
                var figure = reader.Int32();
                var type = reader.Byte();
                if (type is 0 or > GeometryCollection)
                {
                    throw new NotSupportedException(
                        $"SQL Server spatial shape type {type} (a curve or FULLGLOBE) has no well-known binary form.");
                }

                shapes[i] = new Shape(parent, figure, type);
            }
        }

        if (shapes.Length == 0)
        {
            throw new FormatException("SQL Server spatial data holds no shape.");
        }

        var writer = new WkbWriter(x, y, z, m, figureOffsets, shapes);
        writer.WriteShape(0);
        return (srid, writer.ToArray());
    }

    private readonly record struct Shape(int Parent, int FigureOffset, byte Type);

    private sealed class WkbWriter
    {
        private readonly double[] _x;
        private readonly double[] _y;
        private readonly double[]? _z;
        private readonly double[]? _m;
        private readonly int[] _figureOffsets;
        private readonly Shape[] _shapes;
        private readonly List<byte> _output = new(64);

        public WkbWriter(double[] x, double[] y, double[]? z, double[]? m, int[] figureOffsets, Shape[] shapes)
        {
            _x = x;
            _y = y;
            _z = z;
            _m = m;
            _figureOffsets = figureOffsets;
            _shapes = shapes;
        }

        public byte[] ToArray() => _output.ToArray();

        public void WriteShape(int index)
        {
            var shape = _shapes[index];
            var type = (uint)shape.Type + (_z != null ? 1000u : 0u) + (_m != null ? 2000u : 0u);
            _output.Add(1); // little endian
            UInt32(type);

            var (firstFigure, endFigure) = FiguresOf(index);
            switch (shape.Type)
            {
                case Point:
                    if (firstFigure == endFigure)
                    {
                        WriteEmptyPoint();
                    }
                    else
                    {
                        WritePoint(_figureOffsets[firstFigure]);
                    }

                    return;
                case LineString:
                    if (firstFigure == endFigure)
                    {
                        UInt32(0);
                    }
                    else
                    {
                        WritePointList(firstFigure);
                    }

                    return;
                case Polygon:
                    UInt32((uint)(endFigure - firstFigure));
                    for (var f = firstFigure; f < endFigure; f++)
                    {
                        WritePointList(f);
                    }

                    return;
                default:
                    var children = new List<int>();
                    for (var i = index + 1; i < _shapes.Length; i++)
                    {
                        if (_shapes[i].Parent == index)
                        {
                            children.Add(i);
                        }
                    }

                    UInt32((uint)children.Count);
                    foreach (var child in children)
                    {
                        WriteShape(child);
                    }

                    return;
            }
        }

        // A shape's figures run from its own offset to the next later shape's offset; shapes are
        // stored depth-first, so for a leaf that is exactly its own figures.
        private (int First, int End) FiguresOf(int index)
        {
            var first = _shapes[index].FigureOffset;
            if (first < 0)
            {
                return (0, 0);
            }

            var end = _figureOffsets.Length;
            for (var i = index + 1; i < _shapes.Length; i++)
            {
                if (_shapes[i].FigureOffset >= 0)
                {
                    end = _shapes[i].FigureOffset;
                    break;
                }
            }

            return (first, end);
        }

        private void WritePointList(int figure)
        {
            var start = _figureOffsets[figure];
            var end = figure + 1 < _figureOffsets.Length ? _figureOffsets[figure + 1] : _x.Length;
            UInt32((uint)(end - start));
            for (var p = start; p < end; p++)
            {
                WritePoint(p);
            }
        }

        private void WritePoint(int p)
        {
            Double(_x[p]);
            Double(_y[p]);
            if (_z != null)
            {
                Double(_z[p]);
            }

            if (_m != null)
            {
                Double(_m[p]);
            }
        }

        private void WriteEmptyPoint()
        {
            var ordinates = 2 + (_z != null ? 1 : 0) + (_m != null ? 1 : 0);
            for (var i = 0; i < ordinates; i++)
            {
                Double(double.NaN);
            }
        }

        private void UInt32(uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            foreach (var b in buffer)
            {
                _output.Add(b);
            }
        }

        private void Double(double value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
            foreach (var b in buffer)
            {
                _output.Add(b);
            }
        }
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        public Reader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
        }

        public byte Byte()
        {
            Need(1);
            return _data[_position++];
        }

        public int Int32()
        {
            Need(4);
            var value = BinaryPrimitives.ReadInt32LittleEndian(_data[_position..]);
            _position += 4;
            return value;
        }

        public int Count()
        {
            var count = Int32();
            if (count < 0 || count > _data.Length)
            {
                throw new FormatException("SQL Server spatial data has an invalid element count.");
            }

            return count;
        }

        public double Double()
        {
            Need(8);
            var value = BinaryPrimitives.ReadDoubleLittleEndian(_data[_position..]);
            _position += 8;
            return value;
        }

        public double[] Doubles(int count)
        {
            var values = new double[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = Double();
            }

            return values;
        }

        private readonly void Need(int bytes)
        {
            if (_position + bytes > _data.Length)
            {
                throw new FormatException("SQL Server spatial data is truncated.");
            }
        }
    }
}
