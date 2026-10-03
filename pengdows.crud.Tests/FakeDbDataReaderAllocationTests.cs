using System;
using System.Collections.Generic;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// fakeDb backs the library's allocation tests and benchmarks, so reading a value must not allocate
/// on its own: GetValue and the typed getters copied the row's keys into a new array on every call.
/// </summary>
public sealed class FakeDbDataReaderAllocationTests
{
    [Fact]
    public void GetValueAndTypedGetters_OnTheCurrentRow_AllocateNothing()
    {
        var rows = new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["v"] = 12.5m, ["d"] = 3.25 },
            new Dictionary<string, object> { ["id"] = 2, ["v"] = 13.5m, ["d"] = 4.25 }
        };
        using var reader = new fakeDbDataReader(rows);
        reader.Read();
        reader.GetValue(1);
        reader.GetDecimal(1);
        reader.GetDouble(2);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            reader.GetValue(0);
            reader.GetDecimal(1);
            reader.GetDouble(2);
            reader.GetInt32(0);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void GetValue_AfterAdvancing_ReadsTheNewRowsColumns()
    {
        var rows = new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["name"] = "a" },
            new Dictionary<string, object> { ["name"] = "b", ["id"] = 2 }
        };
        using var reader = new fakeDbDataReader(rows);

        reader.Read();
        Assert.Equal(1, reader.GetValue(0));
        reader.Read();
        Assert.Equal("b", reader.GetValue(0));
    }
}
