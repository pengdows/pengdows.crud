using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Snowflake returns a FLOAT's infinity as the text "inf" and "-inf", and its driver's typed reads (GetDouble, GetFloat,
/// GetValue) throw on that text (live probe, 2026-11-22); GetString returns it. A floating-point property holding the
/// infinity must still read as the infinity.
/// </summary>
public class SnowflakeInfinityReadTests
{
    public sealed class FloatHolder
    {
        public int Id { get; set; }
        public float F { get; set; }
    }

    public sealed class DoubleHolder
    {
        public int Id { get; set; }
        public double D { get; set; }
    }

    private static fakeDbDataReader Reader(string column, string text) =>
        new(new List<Dictionary<string, object>> { new() { ["id"] = 1, [column] = text } })
        {
            FloatingTextColumns = new HashSet<string> { column }
        };

    [Fact]
    public async Task FloatColumn_HoldingInfText_ReadsPositiveInfinity()
    {
        await using var reader = Reader("f", "inf");

        var rows = await DataReaderMapper.LoadAsync<FloatHolder>(reader, MapperOptions.Default);

        Assert.Equal(float.PositiveInfinity, rows[0].F);
    }

    [Fact]
    public async Task DoubleColumn_HoldingNegativeInfText_ReadsNegativeInfinity()
    {
        await using var reader = Reader("d", "-inf");

        var rows = await DataReaderMapper.LoadAsync<DoubleHolder>(reader, MapperOptions.Default);

        Assert.Equal(double.NegativeInfinity, rows[0].D);
    }
}
