using System;

namespace pengdows.crud.types.valueobjects;

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