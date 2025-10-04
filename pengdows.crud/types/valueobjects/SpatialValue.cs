using System;

namespace pengdows.crud.types.valueobjects;

public abstract class SpatialValue : IEquatable<SpatialValue>
{
    protected SpatialValue(
        int srid,
        SpatialFormat format,
        ReadOnlyMemory<byte> wellKnownBinary,
        string? wellKnownText,
        string? geoJson,
        object? providerValue = null)
    {
        if (srid < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(srid), srid, "SRID must be non-negative.");
        }

        Srid = srid;
        Format = format;
        WellKnownBinary = wellKnownBinary;
        WellKnownText = wellKnownText;
        GeoJson = geoJson;
        ProviderValue = providerValue;
    }

    public int Srid { get; }
    public SpatialFormat Format { get; }
    public ReadOnlyMemory<byte> WellKnownBinary { get; }
    public string? WellKnownText { get; }
    public string? GeoJson { get; }
    public object? ProviderValue { get; }

    public bool Equals(SpatialValue? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        var sameBinary = WellKnownBinary.Span.SequenceEqual(other.WellKnownBinary.Span);
        return Srid == other.Srid
               && Format == other.Format
               && string.Equals(WellKnownText, other.WellKnownText, StringComparison.Ordinal)
               && string.Equals(GeoJson, other.GeoJson, StringComparison.Ordinal)
               && sameBinary;
    }

    public override bool Equals(object? obj)
    {
        return obj is SpatialValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Srid, Format, WellKnownText, GeoJson, ComputeBinaryHash());
    }

    public override string ToString()
    {
        return Format switch
        {
            SpatialFormat.WellKnownText => WellKnownText ?? string.Empty,
            SpatialFormat.GeoJson => GeoJson ?? string.Empty,
            _ => Convert.ToBase64String(WellKnownBinary.ToArray())
        };
    }

    protected static ReadOnlyMemory<byte> Clone(ReadOnlySpan<byte> buffer)
    {
        return buffer.Length == 0 ? ReadOnlyMemory<byte>.Empty : buffer.ToArray();
    }

    private int ComputeBinaryHash()
    {
        if (WellKnownBinary.IsEmpty)
        {
            return 0;
        }

        var span = WellKnownBinary.Span;
        var hash = 17;
        for (var i = 0; i < span.Length; i++)
        {
            hash = (hash * 31) + span[i];
        }
        return hash;
    }
}
