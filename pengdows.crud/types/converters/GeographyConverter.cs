using System;
using System.Buffers.Binary;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

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
