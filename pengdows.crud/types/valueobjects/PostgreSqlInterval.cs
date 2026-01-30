// =============================================================================
// FILE: PostgreSqlInterval.cs
// PURPOSE: Immutable value object for PostgreSQL INTERVAL type.
//
// AI SUMMARY:
// - Represents PostgreSQL INTERVAL with three components matching internal storage.
// - Readonly struct implementing IEquatable<PostgreSqlInterval>.
// - Properties:
//   * Months: int - months component (includes years as months*12)
//   * Days: int - days component (separate from time)
//   * Microseconds: long - sub-day time in microseconds
// - TimeComponent: TimeSpan from Microseconds (Microseconds * 10 ticks).
// - ToTimeSpan(): Converts Days + TimeComponent (loses Months info).
// - FromTimeSpan(): Creates from TimeSpan (Days + microseconds, no months).
// - Thread-safe and immutable.
// =============================================================================

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Immutable value object representing a PostgreSQL INTERVAL.
/// </summary>
/// <remarks>
/// PostgreSQL intervals have three separate components: months, days, and microseconds.
/// This matches the internal storage format and allows precise round-trip conversion.
/// </remarks>
public readonly struct PostgreSqlInterval : IEquatable<PostgreSqlInterval>
{
    public PostgreSqlInterval(int months, int days, long microseconds)
    {
        Months = months;
        Days = days;
        Microseconds = microseconds;
    }

    public int Months { get; }
    public int Days { get; }
    public long Microseconds { get; }

    public TimeSpan TimeComponent => TimeSpan.FromTicks(Microseconds * 10);

    public TimeSpan ToTimeSpan()
    {
        return TimeSpan.FromDays(Days) + TimeComponent;
    }

    public override string ToString()
    {
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0} months {1} days {2} microseconds",
            Months,
            Days,
            Microseconds);
    }

    public bool Equals(PostgreSqlInterval other)
    {
        return Months == other.Months && Days == other.Days && Microseconds == other.Microseconds;
    }

    public override bool Equals(object? obj)
    {
        return obj is PostgreSqlInterval other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Months, Days, Microseconds);
    }

    public static PostgreSqlInterval FromTimeSpan(TimeSpan value)
    {
        var ticks = value.Ticks;
        var microseconds = ticks / 10;
        return new PostgreSqlInterval(0, (int)value.TotalDays, microseconds);
    }
}