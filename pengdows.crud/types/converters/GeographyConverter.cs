using System;
using System.Buffers.Binary;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database geography values and <see cref="Geography"/> value objects.
/// Supports geodetic coordinates on Earth's surface with spherical/ellipsoidal calculations.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>SQL Server:</strong> Maps to GEOGRAPHY type. Uses SqlGeography from Microsoft.SqlServer.Types. Always uses ellipsoidal calculations.</description></item>
/// <item><description><strong>PostgreSQL:</strong> Maps to PostGIS GEOGRAPHY type. Uses WGS84 (SRID 4326) by default for lat/lon.</description></item>
/// <item><description><strong>MySQL:</strong> No native geography type. Use GEOMETRY with SRID 4326 and application-level geodetic functions.</description></item>
/// <item><description><strong>Oracle:</strong> Uses SDO_GEOMETRY with geodetic coordinate system.</description></item>
/// </list>
/// <para><strong>Geometry vs Geography:</strong> Use Geography for latitude/longitude coordinates
/// representing locations on Earth (GPS coordinates, addresses, etc.). Measurements use geodetic
/// distance (great circle). Use Geometry for planar/projected coordinates.</para>
/// <para><strong>SRID default:</strong> Geography defaults to SRID 4326 (WGS84) when no SRID is specified,
/// since most geographic data uses this standard (GPS, GeoJSON, etc.).</para>
/// <para><strong>Supported geography types:</strong></para>
/// <list type="bullet">
/// <item><description>Point (locations), LineString (routes/paths), Polygon (regions)</description></item>
/// <item><description>MultiPoint, MultiLineString, MultiPolygon</description></item>
/// <item><description>GeometryCollection (mixed types)</description></item>
/// </list>
/// <para><strong>Coordinate order:</strong> WKT format is typically "POINT(longitude latitude)" (x y).
/// GeoJSON uses [longitude, latitude] order. Always longitude first, then latitude.</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. Geography value objects are immutable and thread-safe.</para>
/// </remarks>
/// <example>
/// <code>
/// // Entity with geography column
/// [Table("stores")]
/// public class Store
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
///
///     [Column("location", DbType.Object)]
///     public Geography Location { get; set; }
/// }
///
/// // Create with GPS coordinates (New York City)
/// var store = new Store
/// {
///     Location = Geography.FromWellKnownText("POINT(-74.0060 40.7128)") // Lon, Lat
///     // SRID 4326 (WGS84) is applied automatically
/// };
/// await helper.CreateAsync(store);
///
/// // Create with explicit SRID
/// var store2 = new Store
/// {
///     Location = Geography.FromWellKnownText(
///         "POINT(-122.4194 37.7749)", srid: 4326) // San Francisco
/// };
/// await helper.CreateAsync(store2);
///
/// // Create from GeoJSON
/// var store3 = new Store
/// {
///     Location = Geography.FromGeoJson(@"{""type"":""Point"",""coordinates"":[-0.1276,51.5074]}")
///     // London - GeoJSON is [longitude, latitude]
/// };
/// await helper.CreateAsync(store3);
///
/// // Retrieve and use
/// var retrieved = await helper.RetrieveOneAsync(store.Id);
/// Console.WriteLine($"WKT: {retrieved.Location.WellKnownText}");
/// Console.WriteLine($"SRID: {retrieved.Location.Srid}");  // 4326
/// </code>
/// </example>
internal sealed class GeographyConverter : SpatialConverter<Geography>
{
    protected override Geography FromBinary(ReadOnlySpan<byte> wkb, SupportedDatabase provider)
    {
        ExtractSridFromEwkb(wkb, out var srid, out var normalizedBytes);
        if (srid == 0)
        {
            srid = 4326;
        }
        return Geography.FromWellKnownBinary(normalizedBytes, srid);
    }

    protected override Geography FromTextInternal(string text, SupportedDatabase provider)
    {
        var (srid, pure) = ExtractSridFromText(text);
        if (srid == 0)
        {
            srid = 4326;
        }
        return Geography.FromWellKnownText(pure, srid);
    }

    protected override Geography FromGeoJsonInternal(string json, SupportedDatabase provider)
    {
        var srid = ExtractSridFromGeoJson(json);
        if (srid == 0)
        {
            srid = 4326;
        }
        return Geography.FromGeoJson(json, srid);
    }

    protected override Geography WrapWithProvider(Geography spatial, object providerValue)
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
        if ((type & sridFlag) == 0 || source.Length < 9)
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
