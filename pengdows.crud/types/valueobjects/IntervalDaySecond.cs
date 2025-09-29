using System;

namespace pengdows.crud.types.valueobjects;

public readonly struct IntervalDaySecond : IEquatable<IntervalDaySecond>
{
    public IntervalDaySecond(int days, TimeSpan time)
    {
        Days = days;
        Time = time;
    }

    public int Days { get; }
    public TimeSpan Time { get; }

    public TimeSpan TotalTime => TimeSpan.FromDays(Days) + Time;

    public override string ToString()
    {
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0} days {1:c}",
            Days,
            Time);
    }

    public bool Equals(IntervalDaySecond other)
    {
        return Days == other.Days && Time.Equals(other.Time);
    }

    public override bool Equals(object? obj)
    {
        return obj is IntervalDaySecond other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Days, Time);
    }

    public static IntervalDaySecond FromTimeSpan(TimeSpan value)
    {
        var days = (int)value.TotalDays;
        var residual = value - TimeSpan.FromDays(days);
        return new IntervalDaySecond(days, residual);
    }
}
