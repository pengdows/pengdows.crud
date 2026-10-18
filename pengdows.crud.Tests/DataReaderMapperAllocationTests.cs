using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Per-row allocation of DataReaderMapper for column/property pairs that have a typed read. TYPE-002
/// added two provider fallbacks that applied to every provider: a decimal column into a double
/// property (ODP.NET beyond-decimal) and a DateTime column into a DateTimeOffset property (Snowflake
/// offset timestamps), each boxing every value through a coercer.
/// </summary>
[Collection("AllocationSerial")]
public sealed class DataReaderMapperAllocationTests
{
    public sealed class DecimalValue { public decimal V { get; set; } }
    public sealed class DoubleValue { public double V { get; set; } }
    public sealed class DateTimeValue { public DateTime V { get; set; } }
    public sealed class OffsetValue { public DateTimeOffset V { get; set; } }
    public sealed class DateOnlyValue { public DateOnly V { get; set; } }
    public sealed class TimeSpanValue { public TimeSpan V { get; set; } }
    public sealed class TimeOnlyValue { public TimeOnly V { get; set; } }
    public sealed class GuidValue { public Guid V { get; set; } }

    private static Task<long> AllocatedPerRowAsync<T>(List<Dictionary<string, object>> rows) where T : class, new() =>
        AllocationMeasurement.LowestAsync(() => AllocatedPerRowOnceAsync<T>(rows));

    private static async Task<long> AllocatedPerRowOnceAsync<T>(List<Dictionary<string, object>> rows) where T : class, new()
    {
        TrackedReader Open() => new(new fakeDbDataReader(rows), new Mock<ITrackedConnection>().Object,
            Mock.Of<IAsyncDisposable>(), false);

        await DataReaderMapper.LoadAsync<T>(Open(), MapperOptions.Default);
        var before = GC.GetAllocatedBytesForCurrentThread();
        await DataReaderMapper.LoadAsync<T>(Open(), MapperOptions.Default);
        return (GC.GetAllocatedBytesForCurrentThread() - before) / rows.Count;
    }

    private static List<Dictionary<string, object>> Rows(Func<int, object> value) =>
        Enumerable.Range(0, 1000).Select(i => new Dictionary<string, object> { ["V"] = value(i) }).ToList();

    [Fact]
    public async Task DecimalColumnIntoDoubleProperty_AllocatesNoMoreThanIntoDecimalProperty()
    {
        var rows = Rows(i => 1234.5678m + i);

        var asDouble = await AllocatedPerRowAsync<DoubleValue>(rows);
        var asDecimal = await AllocatedPerRowAsync<DecimalValue>(rows);

        Assert.True(asDouble <= asDecimal, $"double {asDouble} B/row, decimal {asDecimal} B/row");
    }

    [Fact]
    public async Task DateTimeColumnIntoOffsetProperty_AllocatesOnlyTheLargerEntity()
    {
        var rows = Rows(i => new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i));

        var asOffset = await AllocatedPerRowAsync<OffsetValue>(rows);
        var asDateTime = await AllocatedPerRowAsync<DateTimeValue>(rows);

        // A DateTimeOffset property makes the entity 8 bytes larger; nothing else should differ.
        Assert.True(asOffset <= asDateTime + 8, $"offset {asOffset} B/row, DateTime {asDateTime} B/row");
    }

    // DRY-003: DataReaderMapper converted these through the boxed coercer; it uses the gateway's
    // TypedCoercions.
    [Fact]
    public async Task DateTimeColumnIntoDateOnlyProperty_AllocatesNoMoreThanIntoDateTimeProperty()
    {
        var rows = Rows(i => new DateTime(2026, 10, 1).AddDays(i));

        var asDateOnly = await AllocatedPerRowAsync<DateOnlyValue>(rows);
        var asDateTime = await AllocatedPerRowAsync<DateTimeValue>(rows);

        Assert.True(asDateOnly <= asDateTime, $"DateOnly {asDateOnly} B/row, DateTime {asDateTime} B/row");
    }

    [Fact]
    public async Task TimeSpanColumnIntoTimeOnlyProperty_AllocatesNoMoreThanIntoTimeSpanProperty()
    {
        var rows = Rows(i => TimeSpan.FromSeconds(i));

        var asTimeOnly = await AllocatedPerRowAsync<TimeOnlyValue>(rows);
        var asTimeSpan = await AllocatedPerRowAsync<TimeSpanValue>(rows);

        Assert.True(asTimeOnly <= asTimeSpan, $"TimeOnly {asTimeOnly} B/row, TimeSpan {asTimeSpan} B/row");
    }

    // The gateway's Guid reader: a value that isn't 16 bytes fails as InvalidValueException on both.
    [Fact]
    public async Task BinaryColumnOfTheWrongLengthIntoGuid_FailsAsOnTheGateway()
    {
        var rows = new List<Dictionary<string, object>> { new() { ["V"] = new byte[15] } };
        await using var reader = new TrackedReader(new fakeDbDataReader(rows), new Mock<ITrackedConnection>().Object,
            Mock.Of<IAsyncDisposable>(), false);

        var error = await Assert.ThrowsAsync<pengdows.crud.exceptions.DataMappingException>(
            () => DataReaderMapper.LoadAsync<GuidValue>(reader, new MapperOptions(Strict: true)).AsTask());

        Assert.IsType<pengdows.crud.exceptions.InvalidValueException>(error.InnerException);
    }
}
