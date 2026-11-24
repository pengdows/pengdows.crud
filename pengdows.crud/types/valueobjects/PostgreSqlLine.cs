// =============================================================================
// FILE: PostgreSqlLine.cs
// PURPOSE: Immutable value object for PostgreSQL's native line type: an infinite line A*x + B*y + C = 0.
//
// AI SUMMARY:
// - A, B and C are finite doubles. ToString() writes PostgreSQL's text: "{A,B,C}".
// - Parse() and TryParse() read "{A,B,C}" with optional spaces; anything else, or a non-finite coefficient, is refused.
// - Not PostGIS geometry: a line here has no SRID. Thread-safe and immutable.
// =============================================================================

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// A PostgreSQL native line: the infinite line A*x + B*y + C = 0, with no spatial reference.
/// </summary>
public readonly struct PostgreSqlLine : IEquatable<PostgreSqlLine>
{
    private const string Number = @"([^,{}\s]+)";

    private static readonly Regex LineText = new(
        @"^\s*\{\s*" + Number + @"\s*,\s*" + Number + @"\s*,\s*" + Number + @"\s*\}\s*$", RegexOptions.CultureInvariant);

    /// <summary>Creates the line A*x + B*y + C = 0.</summary>
    public PostgreSqlLine(double a, double b, double c)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c))
        {
            throw new ArgumentOutOfRangeException(nameof(a), "A line's coefficients must be finite.");
        }

        A = a;
        B = b;
        C = c;
    }

    /// <summary>The coefficient of x.</summary>
    public double A { get; }

    /// <summary>The coefficient of y.</summary>
    public double B { get; }

    /// <summary>The constant term.</summary>
    public double C { get; }

    /// <summary>Parses "{A,B,C}" text, refusing anything else with a <see cref="FormatException"/>.</summary>
    public static PostgreSqlLine Parse(string text) =>
        TryParse(text, out var line)
            ? line
            : throw new FormatException($"'{text}' is not a PostgreSQL line: expected {{A,B,C}}.");

    /// <summary>Tries to parse "{A,B,C}" text.</summary>
    public static bool TryParse(string? text, out PostgreSqlLine line)
    {
        line = default;
        if (text is null)
        {
            return false;
        }

        var match = LineText.Match(text);
        if (!match.Success ||
            !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ||
            !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ||
            !double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var c) ||
            !double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c))
        {
            return false;
        }

        line = new PostgreSqlLine(a, b, c);
        return true;
    }

    /// <summary>The PostgreSQL text of the line: "{A,B,C}".</summary>
    public override string ToString() =>
        "{" + A.ToString("R", CultureInfo.InvariantCulture) + "," +
        B.ToString("R", CultureInfo.InvariantCulture) + "," +
        C.ToString("R", CultureInfo.InvariantCulture) + "}";

    /// <inheritdoc />
    public bool Equals(PostgreSqlLine other) => A == other.A && B == other.B && C == other.C;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PostgreSqlLine other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(A, B, C);

    /// <summary>Equal coefficients.</summary>
    public static bool operator ==(PostgreSqlLine left, PostgreSqlLine right) => left.Equals(right);

    /// <summary>Different coefficients.</summary>
    public static bool operator !=(PostgreSqlLine left, PostgreSqlLine right) => !left.Equals(right);
}
