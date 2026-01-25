using System;

namespace pengdows.crud.types.valueobjects;

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