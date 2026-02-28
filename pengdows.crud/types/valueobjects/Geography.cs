// =============================================================================
// FILE: Geography.cs
// PURPOSE: Immutable value object for geodetic (Earth surface) spatial geography.
//
// AI SUMMARY:
// - Represents geodetic coordinates on Earth's surface with spherical calculations.
// - Extends SpatialValue with factory methods for different formats.
// - Factory methods:
//   * FromWellKnownBinary(): Create from WKB byte array
//   * FromWellKnownText(): Create from WKT string (e.g., "POINT(-74.0060 40.7128)")
//   * FromGeoJson(): Create from GeoJSON string
// - WithProviderValue(): Clone with provider-specific value attached.
// - SRID typically 4326 (WGS84) for GPS coordinates.
// - Use for lat/lon coordinates, GPS positions, addresses, routes.
// - Use Geometry for planar/projected coordinates (maps, engineering).
// - Thread-safe and immutable.
// =============================================================================

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Immutable value object representing geodetic (Earth surface) geography.
/// </summary>
/// <remarks>
/// Use Geography for latitude/longitude coordinates representing locations
/// on Earth where geodetic (great circle) distance calculations are appropriate.
/// SRID is typically 4326 (WGS84) for GPS coordinates.
/// For planar/projected coordinates, use <see cref="Geometry"/>.
/// </remarks>
public sealed class Geography : SpatialValue
{
    private Geography(
        int srid,
        SpatialFormat format,
        ReadOnlyMemory<byte> wkb,
        string? wkt,
        string? geoJson,
        object? providerValue)
        : base(srid, format, wkb, wkt, geoJson, providerValue)
    {
    }

    public static Geography FromWellKnownBinary(ReadOnlySpan<byte> wkb, int srid, object? providerValue = null)
    {
        return new Geography(srid, SpatialFormat.WellKnownBinary, Clone(wkb), null, null, providerValue);
    }

    public static Geography FromWellKnownText(string wkt, int srid, object? providerValue = null)
    {
        if (string.IsNullOrWhiteSpace(wkt))
        {
            throw new ArgumentException("WKT cannot be empty.", nameof(wkt));
        }

        return new Geography(srid, SpatialFormat.WellKnownText, ReadOnlyMemory<byte>.Empty, wkt, null, providerValue);
    }

    public static Geography FromGeoJson(string geoJson, int srid, object? providerValue = null)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
        {
            throw new ArgumentException("GeoJSON cannot be empty.", nameof(geoJson));
        }

        return new Geography(srid, SpatialFormat.GeoJson, ReadOnlyMemory<byte>.Empty, null, geoJson, providerValue);
    }

    public Geography WithProviderValue(object? providerValue)
    {
        return new Geography(Srid, Format, WellKnownBinary, WellKnownText, GeoJson, providerValue);
    }
}