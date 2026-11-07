using System;
using System.Collections.Generic;
using System.Globalization;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// Npgsql (and CockroachDB and YugabyteDB through it) reports a NUMERIC column as decimal, and its typed read
/// throws OverflowException beyond decimal's range, while the text of the number is still readable. fakeDb's
/// DecimalTextColumns reproduces that, so a reader that only handles Int64-reported columns can be tested
/// against it.
/// </summary>
public class FakeDbDecimalTextColumnTests
{
    private const string Wide = "123456789012345678901234567890";   // 30 digits: beyond decimal's ~7.9e28

    private static fakeDbDataReader ReaderFor(string value)
    {
        var rows = new List<Dictionary<string, object>> { new() { ["n"] = value } };
        return new fakeDbDataReader(rows) { DecimalTextColumns = new HashSet<string> { "n" } };
    }

    [Fact]
    public void DecimalTextColumn_ReportsDecimal()
    {
        using var reader = ReaderFor(Wide);
        Assert.True(reader.Read());

        Assert.Equal(typeof(decimal), reader.GetFieldType(0));
    }

    [Fact]
    public void DecimalTextColumn_GetValueBeyondDecimal_ThrowsOverflow()
    {
        using var reader = ReaderFor(Wide);
        Assert.True(reader.Read());

        var ex = Assert.Throws<OverflowException>(() => reader.GetValue(0));
        Assert.Contains("too large or too small for a Decimal", ex.Message);
    }

    [Fact]
    public void DecimalTextColumn_GetDecimalBeyondDecimal_ThrowsOverflow()
    {
        using var reader = ReaderFor(Wide);
        Assert.True(reader.Read());

        Assert.Throws<OverflowException>(() => reader.GetDecimal(0));
    }

    // Npgsql's IsDBNull reads the row's NULL flag and never decodes the number, so a NUMERIC beyond decimal is not NULL
    // and asking whether it is NULL does not throw. Only the typed getters throw.
    [Fact]
    public void DecimalTextColumn_IsDBNullBeyondDecimal_IsFalseAndDoesNotThrow()
    {
        using var reader = ReaderFor(Wide);
        Assert.True(reader.Read());

        Assert.False(reader.IsDBNull(0));
    }

    [Fact]
    public void DecimalTextColumn_IsDBNullOnANullCell_IsTrue()
    {
        var rows = new List<Dictionary<string, object>> { new() { ["n"] = DBNull.Value } };
        using var reader = new fakeDbDataReader(rows) { DecimalTextColumns = new HashSet<string> { "n" } };
        Assert.True(reader.Read());

        Assert.True(reader.IsDBNull(0));
    }

    [Fact]
    public void DecimalTextColumn_GetStringReturnsTheDigits()
    {
        using var reader = ReaderFor(Wide);
        Assert.True(reader.Read());

        Assert.Equal(Wide, reader.GetString(0));
    }

    [Fact]
    public void DecimalTextColumn_WithinDecimalRange_ReadsAsDecimal()
    {
        using var reader = ReaderFor("12.5");
        Assert.True(reader.Read());

        Assert.Equal(12.5m, reader.GetDecimal(0));
        Assert.Equal(12.5m, reader.GetValue(0));
    }

    [Fact]
    public void Without_DecimalTextColumns_TheValueIsUnchanged()
    {
        var rows = new List<Dictionary<string, object>> { new() { ["n"] = Wide } };
        using var reader = new fakeDbDataReader(rows);
        Assert.True(reader.Read());

        Assert.Equal(Wide, reader.GetValue(0));
    }
}
