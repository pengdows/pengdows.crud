namespace pengdows.crud.types.valueobjects;

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