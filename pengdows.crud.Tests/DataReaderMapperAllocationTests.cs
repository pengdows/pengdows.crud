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
public sealed class DataReaderMapperAllocationTests
{
    public sealed class DecimalValue { public decimal V { get; set; } }
    public sealed class DoubleValue { public double V { get; set; } }
    public sealed class DateTimeValue { public DateTime V { get; set; } }
    public sealed class OffsetValue { public DateTimeOffset V { get; set; } }

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
}
