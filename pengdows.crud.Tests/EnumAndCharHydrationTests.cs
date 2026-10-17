using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// Enum and char columns are read through a typed fast path where the stored value is certainly valid, and through the
// full conversion for everything else. Two things hold: what is mapped is exactly what the full conversion gives
// (differential), and the fast path does not allocate per row (it used to box the value, and the result).
[Collection("TypeRegistry")]
public class EnumAndCharHydrationTests
{
    public enum Mood { Sad = 0, Ok = 1, Happy = 2 }

    [Flags]
    public enum Perms { None = 0, Read = 1, Write = 2, Exec = 4 }

    public sealed class MoodRow { public Mood V { get; set; } }
    public sealed class MoodNullableRow { public Mood? V { get; set; } }
    public sealed class PermsRow { public Perms V { get; set; } }
    public sealed class CharRow { public char V { get; set; } }
    public sealed class IntRow { public int V { get; set; } }

    private static fakeDbDataReader Reader(Type column, object value) => new(new List<Dictionary<string, object>>
    {
        new(StringComparer.OrdinalIgnoreCase) { ["V"] = value }
    }) { Columns = [new fakeDbColumn("V", column, "X")] };

    private static async Task<T> Map<T>(Type column, object value) where T : class, new()
    {
        var original = TypeCoercionHelper.Logger;
        try
        {
            TypeCoercionHelper.Logger = NullLogger.Instance;
            var rows = await DataReaderMapper.LoadAsync<T>(Reader(column, value), MapperOptions.Default);
            return Assert.Single(rows);
        }
        finally
        {
            TypeCoercionHelper.Logger = original;
        }
    }

    // What the full conversion gives for the same stored value, or the property's default when it cannot convert.
    private static object? Reference(Type column, object value, Type target)
    {
        try
        {
            return TypeCoercionHelper.Coerce(value, column, target, TypeCoercionOptions.Default);
        }
        catch
        {
            return null;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]    // undefined
    [InlineData(-3)]   // undefined, negative
    public async Task IntColumnIntoEnum_MatchesTheFullConversion(int stored)
    {
        var row = await Map<MoodRow>(typeof(int), stored);

        var expected = Reference(typeof(int), stored, typeof(Mood));
        Assert.Equal(expected is Mood m ? m : default, row.V);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]    // Read | Write
    [InlineData(7)]
    [InlineData(8)]    // undefined
    public async Task IntColumnIntoFlagsEnum_MatchesTheFullConversion(int stored)
    {
        var row = await Map<PermsRow>(typeof(int), stored);

        var expected = Reference(typeof(int), stored, typeof(Perms));
        Assert.Equal(expected is Perms p ? p : default, row.V);
    }

    [Theory]
    [InlineData("Ok")]
    [InlineData("ok")]
    [InlineData("Happy")]
    [InlineData("1")]
    [InlineData("nope")]
    public async Task TextColumnIntoEnum_MatchesTheFullConversion(string stored)
    {
        var row = await Map<MoodRow>(typeof(string), stored);

        var expected = Reference(typeof(string), stored, typeof(Mood));
        Assert.Equal(expected is Mood m ? m : default, row.V);
    }

    [Fact]
    public async Task NullIntoNullableEnum_LeavesItNull_AndAValueMapsIt()
    {
        Assert.Null((await Map<MoodNullableRow>(typeof(int), DBNull.Value)).V);
        Assert.Equal(Mood.Happy, (await Map<MoodNullableRow>(typeof(int), 2)).V);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Z")]
    [InlineData("ab")] // not one character: the full conversion decides
    public async Task TextColumnIntoChar_MatchesTheFullConversion(string stored)
    {
        var row = await Map<CharRow>(typeof(string), stored);

        var expected = Reference(typeof(string), stored, typeof(char));
        Assert.Equal(expected is char c ? c : default, row.V);
    }

    // Allocated bytes per mapped row, after the mapping is resolved and warm.
    private static long PerRow<T>(Type column, object value) where T : class, new()
    {
        const int rows = 2000;
        var data = new List<Dictionary<string, object>>();
        for (var i = 0; i < rows; i++)
        {
            data.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["V"] = value });
        }

        var columns = new[] { new fakeDbColumn("V", column, "X") };
        using var schema = new fakeDbDataReader(data) { Columns = columns };
        var mapper = DataReaderMapper.ResolveRowMapper<T>(schema, MapperOptions.Default);

        long Pass()
        {
            using var reader = new fakeDbDataReader(data) { Columns = columns };
            var before = GC.GetAllocatedBytesForCurrentThread();
            while (reader.Read())
            {
                mapper(reader);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Pass(); // warm
        return Pass() / rows;
    }

    [Fact]
    public void IntColumnIntoEnum_AllocatesNoMoreThanIntoAnInt()
    {
        var baseline = PerRow<IntRow>(typeof(int), 1);

        Assert.True(PerRow<MoodRow>(typeof(int), 1) <= baseline + 8,
            "an int column read into an enum should not box the value or the result");
    }

    [Fact]
    public void TextColumnIntoChar_AllocatesNoMoreThanIntoAnInt()
    {
        var baseline = PerRow<IntRow>(typeof(int), 1);

        Assert.True(PerRow<CharRow>(typeof(string), "a") <= baseline + 8,
            "a one-character text column read into a char should not box the result");
    }
}
