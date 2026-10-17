using System;
using System.Collections.Generic;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A provider's IsDBNull is a flag read. fakeDb's was a full GetValue plus a pass over every quirk predicate, which
/// made the null check look as expensive as a read and skewed every benchmark that asks it (a nullable column).
/// A reader with no quirk configured answers from the cell, without a read.
/// </summary>
public class FakeDbIsDbNullCostTests
{
    private sealed class CountingReader : fakeDbDataReader
    {
        public int GetValueCalls;

        public CountingReader(List<Dictionary<string, object>> rows) : base(rows)
        {
        }

        public override object GetValue(int i)
        {
            GetValueCalls++;
            return base.GetValue(i);
        }
    }

    private static CountingReader Reader(object? value)
    {
        var reader = new CountingReader(new List<Dictionary<string, object>>
        {
            new(StringComparer.OrdinalIgnoreCase) { ["V"] = value! }
        });
        Assert.True(reader.Read());
        return reader;
    }

    [Fact]
    public void IsDBNull_OnAPlainReader_DoesNotRead()
    {
        using var reader = Reader(7);

        Assert.False(reader.IsDBNull(0));
        Assert.Equal(0, reader.GetValueCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("DBNull")]
    public void IsDBNull_ForANullCell_IsTrueWithoutARead(string? marker)
    {
        using var reader = Reader(marker == null ? null : DBNull.Value);

        Assert.True(reader.IsDBNull(0));
        Assert.Equal(0, reader.GetValueCalls);
    }

    [Fact]
    public void IsDBNull_WhenAColumnReadFails_StillFails()
    {
        using var reader = Reader(7);
        reader.ColumnReadExceptions = new Dictionary<string, Exception> { ["V"] = new OverflowException("x") };

        Assert.Throws<OverflowException>(() => reader.IsDBNull(0));
    }
}
