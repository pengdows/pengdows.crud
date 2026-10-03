using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-014: with its plan cached, DataReaderMapper.LoadAsync allocated a closure and delegate for
/// the plan-cache lookup on every call (BoundedCache.GetOrAdd's factory captured the reader and
/// options). Beyond reading the rows by hand, a cached call now allocates only its lookup key's
/// column name and type arrays.
/// </summary>
public sealed class DataReaderMapperPlanLookupAllocationTests
{
    private readonly ITestOutputHelper _output;

    public DataReaderMapperPlanLookupAllocationTests(ITestOutputHelper output) => _output = output;

    public sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    private static List<Dictionary<string, object>> Rows() =>
        Enumerable.Range(1, 4).Select(i => new Dictionary<string, object>
        {
            ["Id"] = i, ["Name"] = "n", ["Value"] = i
        }).ToList();

    private static async Task<long> MeasureAsync(Func<fakeDbDataReader, Task> read)
    {
        var rows = Rows();
        for (var i = 0; i < 3; i++)
        {
            using var warm = new fakeDbDataReader(rows);
            await read(warm);
        }

        long total = 0;
        for (var i = 0; i < 50; i++)
        {
            using var reader = new fakeDbDataReader(rows);
            var before = GC.GetAllocatedBytesForCurrentThread();
            await read(reader);
            total += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return total / 50;
    }

    [Fact]
    public async Task CachedPlanCall_AllocatesOnlyTheRowsAndTheLookupKey()
    {
        var mapper = await AllocationMeasurement.LowestAsync(() => MeasureAsync(async r =>
            await DataReaderMapper.LoadAsync<Row>(r, MapperOptions.Default)));
        var byHand = await AllocationMeasurement.LowestAsync(() => MeasureAsync(async r =>
        {
            var list = new List<Row>();
            while (await r.ReadAsync())
            {
                list.Add(new Row { Id = r.GetInt32(0), Name = r.GetString(1), Value = r.GetInt32(2) });
            }
        }));

        // The cache key's column names and types: a string[3] and a Type[3].
        var lookupKey = 2 * (IntPtr.Size * 3 + 24L);
        _output.WriteLine($"mapper {mapper} B, by hand {byHand} B, key arrays {lookupKey} B");
        Assert.True(mapper <= byHand + lookupKey, $"mapper {mapper} B, by hand {byHand} B + key {lookupKey} B");
    }
}
