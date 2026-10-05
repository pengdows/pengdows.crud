// =============================================================================
// FILE: GeographyConverter.cs
// PURPOSE: Converter for geodetic geography (Earth surface) spatial types.
//
// AI SUMMARY:
// - Converts between database spatial values and Geography value objects.
// - Supports geodetic coordinates on Earth's surface with spherical calculations.
// - Provider-specific:
//   * SQL Server: GEOGRAPHY type, no Microsoft.SqlServer.Types needed (SqlServerSpatialFormat)
//   * PostgreSQL: PostGIS GEOGRAPHY (WGS84 SRID 4326 default)
//   * MySQL: No native geography; use GEOMETRY with SRID 4326
//   * Oracle: SDO_GEOMETRY with geodetic coordinate system
// - SRID defaults to 4326 (WGS84) when a database value carries none - standard for GPS/GeoJSON.
// - FromBinary(): Converts WKB/EWKB to Geography (defaults SRID to 4326).
// - FromTextInternal(): Converts WKT/EWKT (defaults SRID to 4326).
// - FromGeoJsonInternal(): Converts GeoJSON (defaults SRID to 4326).
// - Coordinate order: WKT is "POINT(longitude latitude)", GeoJSON is [lon, lat].
// - Use Geography for lat/lon coordinates (GPS, addresses, routes).
// - Use Geometry for planar/projected coordinates (maps, engineering).
// =============================================================================

using System.Buffers.Binary;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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
/// <item><description><strong>SQL Server:</strong> Maps to GEOGRAPHY type. Written as SRID-prefixed WKB built server-side with STGeomFromWKB, read from the stored encoding; Microsoft.SqlServer.Types is not needed (a loaded SqlGeography is still read). Always uses ellipsoidal calculations.</description></item>
/// <item><description><strong>PostgreSQL:</strong> Maps to PostGIS GEOGRAPHY type. Uses WGS84 (SRID 4326) by default for lat/lon.</description></item>
/// <item><description><strong>MySQL:</strong> No native geography type. Use GEOMETRY with SRID 4326 and application-level geodetic functions.</description></item>
/// <item><description><strong>Oracle:</strong> Uses SDO_GEOMETRY with geodetic coordinate system.</description></item>
/// </list>
/// <para><strong>Geometry vs Geography:</strong> Use Geography for latitude/longitude coordinates
/// representing locations on Earth (GPS coordinates, addresses, etc.). Measurements use geodetic
/// distance (great circle). Use Geometry for planar/projected coordinates.</para>
/// <para><strong>SRID default:</strong> When reading a database value that carries no SRID, the converter defaults to SRID 4326 (WGS84),
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
///     Location = Geography.FromWellKnownText("POINT(-74.0060 40.7128)", srid: 4326) // Lon, Lat
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
///     Location = Geography.FromGeoJson(@"{""type"":""Point"",""coordinates"":[-0.1276,51.5074]}", srid: 4326)
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
        GeometryConverter.ExtractSridFromEwkb(wkb, out var srid, out var normalizedBytes);
        if (srid == 0)
        {
            srid = 4326;
        }

        return Geography.FromWellKnownBinary(normalizedBytes, srid);
    }

    protected override Geography FromTextInternal(string text, SupportedDatabase provider)
    {
        var (srid, pure) = GeometryConverter.ExtractSridFromText(text);
        if (srid == 0)
        {
            srid = 4326;
        }

        return Geography.FromWellKnownText(pure, srid);
    }

    protected override Geography FromGeoJsonInternal(string json, SupportedDatabase provider)
    {
        var srid = GeometryConverter.ExtractSridFromGeoJson(json);
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

    protected override Geography FromBinaryWithSrid(ReadOnlySpan<byte> wkb, int srid, object providerValue)
    {
        return Geography.FromWellKnownBinary(wkb, srid, providerValue);
    }
}
