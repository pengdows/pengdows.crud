// =============================================================================
// FILE: SpatialFormat.cs
// PURPOSE: Enum defining spatial data representation formats.
//
// AI SUMMARY:
// - Enum identifying how spatial data is represented in a SpatialValue.
// - Values:
//   * WellKnownBinary: Binary format (WKB/EWKB) - compact, efficient
//   * WellKnownText: Text format (WKT/EWKT) - human-readable (e.g., "POINT(1 2)")
//   * GeoJson: JSON format - web-friendly, interoperable
// - Used by SpatialValue.Format to track original data format.
// - Converters use this to determine optimal output format per provider.
// =============================================================================

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Specifies the representation format of spatial data.
/// </summary>
public enum SpatialFormat
{
    WellKnownBinary,
    WellKnownText,
    GeoJson
}