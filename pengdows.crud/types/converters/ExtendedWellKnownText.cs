// =============================================================================
// FILE: ExtendedWellKnownText.cs
// PURPOSE: A spatial value as EWKT ("SRID=n;<wkt>"), the SRID always present.
//
// AI SUMMARY:
// - From(): the value's own WKT (any SRID prefix replaced by the value's SRID) or its WKB decoded.
// - A GeoJSON-only value throws NotSupportedException.
// - Used where a database builds geometries only from text (Oracle SDO_GEOMETRY, TYPE-021).
// =============================================================================

using System.Globalization;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

internal static class ExtendedWellKnownText
{
    public static string From(SpatialValue value)
    {
        return string.Concat("SRID=", value.Srid.ToString(CultureInfo.InvariantCulture), ";", WellKnownTextOf(value));
    }

    // The value as plain WKT: its own text (any SRID prefix dropped), or its WKB decoded.
    private static string WellKnownTextOf(SpatialValue value)
    {
        if (!string.IsNullOrEmpty(value.WellKnownText))
        {
            var text = value.WellKnownText.Trim();
            if (text.StartsWith("SRID=", StringComparison.OrdinalIgnoreCase))
            {
                text = text[(text.IndexOf(';') + 1)..];
            }

            return text;
        }

        if (!value.WellKnownBinary.IsEmpty)
        {
            return WellKnownBinaryDecoder.ToWellKnownText(value.WellKnownBinary.Span);
        }

        throw new NotSupportedException("This spatial value holds only GeoJSON; it needs WKT or WKB to be written as EWKT.");
    }
}
