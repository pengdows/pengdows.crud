using System.Buffers.Binary;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database planar geometry values and <see cref="Geometry"/> value objects.
/// Supports 2D/3D/4D geometries in Cartesian coordinate systems (flat-earth model).
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>SQL Server:</strong> Maps to GEOMETRY type. Uses SqlGeometry from Microsoft.SqlServer.Types.</description></item>
/// <item><description><strong>PostgreSQL:</strong> Maps to PostGIS GEOMETRY type. Supports all OGC geometry types.</description></item>
/// <item><description><strong>MySQL:</strong> Maps to GEOMETRY, POINT, LINESTRING, POLYGON types.</description></item>
/// <item><description><strong>Oracle:</strong> Maps to SDO_GEOMETRY with coordinate system.</description></item>
/// </list>
/// <para><strong>Geometry vs Geography:</strong> Use Geometry for planar (flat) coordinates like projected maps,
/// engineering drawings, or local surveys. Measurements are Euclidean distance. Use Geography for
/// lat/lon coordinates on Earth's surface with geodetic calculations.</para>
/// <para><strong>Supported geometry types:</strong></para>
/// <list type="bullet">
/// <item><description>Point, LineString, Polygon</description></item>
/// <item><description>MultiPoint, MultiLineString, MultiPolygon</description></item>
/// <item><description>GeometryCollection (heterogeneous collection)</description></item>
/// <item><description>Supports Z (elevation) and M (measure) coordinates</description></item>
/// </list>
/// <para><strong>Formats:</strong> WKT ("POINT(1 2)"), WKB (binary), EWKT ("SRID=3857;POINT(1 2)"), EWKB, GeoJSON.</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. Geometry value objects are immutable and thread-safe.</para>
/// </remarks>
/// <example>
/// <code>
/// // Entity with geometry column
/// [Table("buildings")]
/// public class Building
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
///
///     [Column("location", DbType.Object)]
///     public Geometry Location { get; set; }
/// }
///
/// // Create with WKT point
/// var building = new Building
/// {
///     Location = Geometry.FromWellKnownText("POINT(100000 200000)", srid: 3857) // Web Mercator
/// };
/// await helper.CreateAsync(building);
///
/// // Create with polygon
/// var building2 = new Building
/// {
///     Location = Geometry.FromWellKnownText(
///         "POLYGON((0 0, 100 0, 100 100, 0 100, 0 0))", srid: 0)
/// };
/// await helper.CreateAsync(building2);
///
/// // Retrieve and use
/// var retrieved = await helper.RetrieveOneAsync(building.Id);
/// Console.WriteLine($"WKT: {retrieved.Location.WellKnownText}");
/// Console.WriteLine($"SRID: {retrieved.Location.Srid}");
/// </code>
/// </example>
internal sealed class GeometryConverter : SpatialConverter<Geometry>
{
    protected override Geometry FromBinary(ReadOnlySpan<byte> wkb, SupportedDatabase provider)
    {
        ExtractSridFromEwkb(wkb, out var srid, out var normalizedBytes);
        return Geometry.FromWellKnownBinary(normalizedBytes, srid);
    }

    protected override Geometry FromTextInternal(string text, SupportedDatabase provider)
    {
        var (srid, pure) = ExtractSridFromText(text);
        return Geometry.FromWellKnownText(pure, srid);
    }

    protected override Geometry FromGeoJsonInternal(string json, SupportedDatabase provider)
    {
        var srid = ExtractSridFromGeoJson(json);
        return Geometry.FromGeoJson(json, srid);
    }

    protected override Geometry WrapWithProvider(Geometry spatial, object providerValue)
    {
        return spatial.WithProviderValue(providerValue);
    }

    private static void ExtractSridFromEwkb(ReadOnlySpan<byte> source, out int srid, out byte[] normalized)
    {
        if (source.Length < 5)
        {
            srid = 0;
            normalized = source.ToArray();
            return;
        }

        var littleEndian = source[0] == 1;
        var type = littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(1, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(source.Slice(1, 4));

        const uint sridFlag = 0x20000000;
        if ((type & sridFlag) == 0)
        {
            srid = 0;
            normalized = source.ToArray();
            return;
        }

        if (source.Length < 9)
        {
            srid = 0;
            normalized = source.ToArray();
            return;
        }

        srid = littleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(source.Slice(5, 4))
            : BinaryPrimitives.ReadInt32BigEndian(source.Slice(5, 4));

        normalized = source.ToArray();
    }

    private static (int srid, string text) ExtractSridFromText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (0, string.Empty);
        }

        if (!text.StartsWith("SRID=", StringComparison.OrdinalIgnoreCase))
        {
            return (0, text);
        }

        var semicolonIndex = text.IndexOf(';');
        if (semicolonIndex < 5)
        {
            return (0, text);
        }

        var sridPart = text.Substring(5, semicolonIndex - 5);
        if (int.TryParse(sridPart, out var srid))
        {
            var pure = text.Substring(semicolonIndex + 1);
            return (srid, pure);
        }

        return (0, text);
    }

    private static int ExtractSridFromGeoJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        var sridIndex = json.IndexOf("\"srid\"", StringComparison.OrdinalIgnoreCase);
        if (sridIndex < 0)
        {
            return 0;
        }

        var colon = json.IndexOf(':', sridIndex);
        if (colon < 0)
        {
            return 0;
        }

        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        for (var i = colon + 1; i < json.Length; i++)
        {
            var c = json[i];
            if (char.IsDigit(c))
            {
                sb.Append(c);
            }
            else if (sb.Length > 0)
            {
                break;
            }
        }

        return int.TryParse(sb.ToString(), out var srid) ? srid : 0;
    }
}