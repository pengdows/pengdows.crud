// =============================================================================
// FILE: Geometry.cs
// PURPOSE: Immutable value object for planar (Cartesian) spatial geometry.
//
// AI SUMMARY:
// - Represents planar geometry in Cartesian coordinate systems (flat-earth model).
// - Extends SpatialValue with factory methods for different formats.
// - Factory methods:
//   * FromWellKnownBinary(): Create from WKB byte array
//   * FromWellKnownText(): Create from WKT string (e.g., "POINT(100 200)")
//   * FromGeoJson(): Create from GeoJSON string
// - WithProviderValue(): Clone with provider-specific value attached.
// - Use for maps, engineering drawings, local surveys with Euclidean distance.
// - Use Geography for lat/lon coordinates on Earth's surface.
// - Thread-safe and immutable.
// =============================================================================

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Immutable value object representing planar (Cartesian) geometry.
/// </summary>
/// <remarks>
/// Use Geometry for planar coordinates in projected maps, engineering drawings,
/// or local surveys where Euclidean distance calculations are appropriate.
/// For latitude/longitude coordinates on Earth's surface, use <see cref="Geography"/>.
/// </remarks>
public sealed class Geometry : SpatialValue
{
    private Geometry(
        int srid,
        SpatialFormat format,
        ReadOnlyMemory<byte> wkb,
        string? wkt,
        string? geoJson,
        object? providerValue)
        : base(srid, format, wkb, wkt, geoJson, providerValue)
    {
    }

    public static Geometry FromWellKnownBinary(ReadOnlySpan<byte> wkb, int srid, object? providerValue = null)
    {
        return new Geometry(srid, SpatialFormat.WellKnownBinary, Clone(wkb), null, null, providerValue);
    }

    public static Geometry FromWellKnownText(string wkt, int srid, object? providerValue = null)
    {
        if (string.IsNullOrWhiteSpace(wkt))
        {
            throw new ArgumentException("WKT cannot be empty.", nameof(wkt));
        }

        return new Geometry(srid, SpatialFormat.WellKnownText, ReadOnlyMemory<byte>.Empty, wkt, null, providerValue);
    }

    public static Geometry FromGeoJson(string geoJson, int srid, object? providerValue = null)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
        {
            throw new ArgumentException("GeoJSON cannot be empty.", nameof(geoJson));
        }

        return new Geometry(srid, SpatialFormat.GeoJson, ReadOnlyMemory<byte>.Empty, null, geoJson, providerValue);
    }

    public Geometry WithProviderValue(object? providerValue)
    {
        return new Geometry(Srid, Format, WellKnownBinary, WellKnownText, GeoJson, providerValue);
    }
}