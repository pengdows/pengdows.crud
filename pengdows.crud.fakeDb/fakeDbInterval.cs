namespace pengdows.crud.fakeDb;

/// <summary>
/// A stored interval as DuckDB.NET 1.5.6 exposes it (its <c>DuckDBInterval</c>, confirmed live): months,
/// days and microseconds, the microseconds a signed value surfaced as <see cref="ulong"/>. A column
/// holding one emulates the driver on INTERVAL: <see cref="fakeDbDataReader.GetFieldType"/> reports
/// <see cref="TimeSpan"/>, <see cref="fakeDbDataReader.GetDataTypeName"/> "Interval",
/// <see cref="fakeDbDataReader.GetProviderSpecificValue"/> returns this value, and
/// <see cref="fakeDbDataReader.GetValue"/> converts it the way the driver does
/// (<see cref="ToDriverTimeSpan"/>).
/// </summary>
public readonly struct fakeDbInterval
{
    public fakeDbInterval(int months, int days, ulong micros)
    {
        Months = months;
        Days = days;
        Micros = micros;
    }

    public int Months { get; }
    public int Days { get; }
    public ulong Micros { get; }

    /// <summary>
    /// The driver's conversion: months &gt;= 1 throw, negative months are dropped, and days plus
    /// microseconds are summed as unsigned, throwing when the sum is above <see cref="long.MaxValue"/>,
    /// i.e. for every negative interval.
    /// </summary>
    public TimeSpan ToDriverTimeSpan()
    {
        if (Months >= 1)
        {
            throw new ArgumentOutOfRangeException("interval",
                "Cannot convert a value of type DuckDBInterval to type TimeSpan when the attribute 'Months' is greater or equal to 1");
        }

        var total = unchecked((ulong)(Days * 86_400_000_000L) + Micros);
        if (total > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException("interval",
                "Cannot convert a value of type DuckDBInterval to type TimeSpan when the value of total microseconds is larger than 9223372036854775807");
        }

        return TimeSpan.FromTicks((long)total * 10);
    }
}
