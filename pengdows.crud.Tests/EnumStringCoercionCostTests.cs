using System;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DataReaderMapper reads a string-stored enum through TypeCoercionHelper's enum coercion, which
/// looked the literal up by reflection on every value (MakeGenericType, GetMethod, MethodInfo.Invoke
/// with an object[]): measured at 374 ns and 235 B per row against 94 ns for a string property.
/// Only the boxed result (24 B) should be allocated per value.
/// </summary>
public sealed class EnumStringCoercionCostTests
{
    public enum Mood
    {
        Happy,
        Sad,
        Ok
    }

    [Fact]
    public void MapperCoercer_StringToEnum_AllocatesOnlyTheBoxedResult()
    {
        var coercer = TypeCoercionHelper.ResolveCoercer(typeof(string), typeof(Mood), EnumParseFailureMode.Throw);
        string[] literals = { "Happy", "Sad", "Ok" };
        for (var i = 0; i < 100; i++)
        {
            coercer(literals[i % 3]);
        }

        const int count = 3000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++)
        {
            coercer(literals[i % 3]);
        }

        var perValue = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)count;

        Assert.True(perValue <= 32, $"{perValue:0.#} bytes per value");
    }

    [Theory]
    [InlineData("Sad", Mood.Sad)]
    [InlineData("sad", Mood.Sad)]
    [InlineData("OK", Mood.Ok)]
    public void MapperCoercer_StringToEnum_ParsesNamesCaseInsensitively(string literal, Mood expected)
    {
        var coercer = TypeCoercionHelper.ResolveCoercer(typeof(string), typeof(Mood), EnumParseFailureMode.Throw);

        Assert.Equal(expected, coercer(literal));
    }

    [Fact]
    public void MapperCoercer_StringToEnum_UnknownLiteralStillFails()
    {
        var coercer = TypeCoercionHelper.ResolveCoercer(typeof(string), typeof(Mood), EnumParseFailureMode.Throw);

        Assert.ThrowsAny<Exception>(() => coercer("Furious"));
    }
}
