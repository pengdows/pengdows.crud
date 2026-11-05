// =============================================================================
// FILE: PostgreSqlPolygon.cs
// PURPOSE: Immutable value object for PostgreSQL's native polygon type: an ordered list of points, no SRID.
//
// AI SUMMARY:
// - Points are the vertices in order. ToString() writes PostgreSQL's text: "((x,y),(x,y),...)".
// - At least one point is required; construction and parsing refuse an empty polygon.
// - Parse() and TryParse() read that text; malformed text is refused.
// - Not PostGIS geometry: use Geometry for a spatial reference.
// - Thread-safe and immutable: the points are copied on construction.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// A PostgreSQL native polygon: its vertices, in order, with no spatial reference.
/// </summary>
public readonly struct PostgreSqlPolygon : IEquatable<PostgreSqlPolygon>
{
    private static readonly Regex PointAt = new(
        @"\G\s*\(\s*([^,()\s]+)\s*,\s*([^,()\s]+)\s*\)\s*(,|$)", RegexOptions.CultureInvariant);

    private readonly PostgreSqlPoint[] _points;

    /// <summary>Creates a polygon from its vertices, in order. At least one is required.</summary>
    public PostgreSqlPolygon(IEnumerable<PostgreSqlPoint> points)
    {
        if (points is null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        _points = points.ToArray();
        if (_points.Length == 0)
        {
            throw new ArgumentException("A polygon needs at least one point.", nameof(points));
        }
    }

    /// <summary>The vertices, in order.</summary>
    public IReadOnlyList<PostgreSqlPoint> Points => _points ?? Array.Empty<PostgreSqlPoint>();

    /// <summary>Parses "((x,y),(x,y),...)" text, refusing anything else with a <see cref="FormatException"/>.</summary>
    public static PostgreSqlPolygon Parse(string text) =>
        TryParse(text, out var polygon)
            ? polygon
            : throw new FormatException($"'{text}' is not a PostgreSQL polygon: expected ((x,y),...).");

    /// <summary>Tries to parse "((x,y),(x,y),...)" text.</summary>
    public static bool TryParse(string? text, out PostgreSqlPolygon polygon)
    {
        polygon = default;
        if (text is null)
        {
            return false;
        }

        var inner = text.Trim();
        if (inner.Length < 2 || inner[0] != '(' || inner[^1] != ')')
        {
            return false;
        }

        var body = inner.Substring(1, inner.Length - 2);
        var points = new List<PostgreSqlPoint>();
        var position = 0;
        while (position < body.Length)
        {
            var match = PointAt.Match(body, position);
            if (!match.Success ||
                !PostgreSqlPoint.TryParse("(" + match.Groups[1].Value + "," + match.Groups[2].Value + ")", out var point))
            {
                return false;
            }

            points.Add(point);
            position = match.Index + match.Length;
        }

        if (points.Count == 0)
        {
            return false;
        }

        polygon = new PostgreSqlPolygon(points);
        return true;
    }

    /// <summary>The PostgreSQL text of the polygon: "((x,y),(x,y),...)".</summary>
    public override string ToString()
    {
        var text = new StringBuilder("(");
        for (var i = 0; i < Points.Count; i++)
        {
            if (i > 0)
            {
                text.Append(',');
            }

            text.Append(Points[i].ToString());
        }

        return text.Append(')').ToString();
    }

    /// <inheritdoc />
    public bool Equals(PostgreSqlPolygon other) => Points.SequenceEqual(other.Points);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PostgreSqlPolygon other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var point in Points)
        {
            hash.Add(point);
        }

        return hash.ToHashCode();
    }

    /// <summary>The same vertices in order.</summary>
    public static bool operator ==(PostgreSqlPolygon left, PostgreSqlPolygon right) => left.Equals(right);

    /// <summary>Different vertices or order.</summary>
    public static bool operator !=(PostgreSqlPolygon left, PostgreSqlPolygon right) => !left.Equals(right);
}
