// =============================================================================
// FILE: PostgreSqlPoint.cs
// PURPOSE: Immutable value object for PostgreSQL's native point type: a coordinate pair, no SRID.
//
// AI SUMMARY:
// - X and Y are finite doubles. ToString() writes PostgreSQL's text: "(x,y)".
// - Parse() and TryParse() read "(x,y)" with optional spaces around the numbers; malformed or non-finite text is refused.
// - Not PostGIS geometry: a PostGIS Geometry carries an SRID, which a native point cannot hold. Use Geometry for that.
// - Thread-safe and immutable.
// =============================================================================

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// A PostgreSQL native point: the coordinates (x, y), with no spatial reference.
/// </summary>
public readonly struct PostgreSqlPoint : IEquatable<PostgreSqlPoint>
{
    private static readonly Regex PointText = new(
        @"^\s*\(\s*([^,()\s]+)\s*,\s*([^,()\s]+)\s*\)\s*$", RegexOptions.CultureInvariant);

    /// <summary>Creates a point at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public PostgreSqlPoint(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "A point's coordinates must be finite.");
        }

        X = x;
        Y = y;
    }

    /// <summary>The x coordinate.</summary>
    public double X { get; }

    /// <summary>The y coordinate.</summary>
    public double Y { get; }

    /// <summary>Parses "(x,y)" text, refusing anything else with a <see cref="FormatException"/>.</summary>
    public static PostgreSqlPoint Parse(string text) =>
        TryParse(text, out var point)
            ? point
            : throw new FormatException($"'{text}' is not a PostgreSQL point: expected (x,y).");

    /// <summary>Tries to parse "(x,y)" text.</summary>
    public static bool TryParse(string? text, out PostgreSqlPoint point)
    {
        point = default;
        if (text is null)
        {
            return false;
        }

        var match = PointText.Match(text);
        if (!match.Success ||
            !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !double.IsFinite(x) || !double.IsFinite(y))
        {
            return false;
        }

        point = new PostgreSqlPoint(x, y);
        return true;
    }

    /// <summary>The PostgreSQL text of the point: "(x,y)".</summary>
    public override string ToString() =>
        "(" + X.ToString("R", CultureInfo.InvariantCulture) + "," + Y.ToString("R", CultureInfo.InvariantCulture) + ")";

    /// <inheritdoc />
    public bool Equals(PostgreSqlPoint other) => X == other.X && Y == other.Y;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PostgreSqlPoint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>Equal coordinates.</summary>
    public static bool operator ==(PostgreSqlPoint left, PostgreSqlPoint right) => left.Equals(right);

    /// <summary>Different coordinates.</summary>
    public static bool operator !=(PostgreSqlPoint left, PostgreSqlPoint right) => !left.Equals(right);
}
