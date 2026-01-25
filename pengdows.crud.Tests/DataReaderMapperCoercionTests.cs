using System;
using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class DataReaderMapperCoercionTests
{
    [Fact]
    public void CoerceValue_ReturnsDefaultWhenEnumParseFails()
    {
        var method = typeof(DataReaderMapper)
                         .GetMethod("CoerceValue", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new InvalidOperationException("CoerceValue method missing");

        var property = typeof(EnumEntity).GetProperty(nameof(EnumEntity.Color))!;
        var result = method.Invoke(null, new object?[]
        {
            "invalid",
            property,
            typeof(string),
            EnumParseFailureMode.SetDefaultValue
        });

        Assert.Equal(Enum.ToObject(typeof(Color), 0), result);
    }

    [Fact]
    public void TryHandleEnumFailure_SetNullAndLog_ReturnsNullForNullableProperty()
    {
        var method = typeof(DataReaderMapper)
                         .GetMethod("TryHandleEnumFailure", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new InvalidOperationException("TryHandleEnumFailure method missing");

        var property = typeof(EnumEntity).GetProperty(nameof(EnumEntity.NullableColor))!;
        var args = new object?[]
        {
            "invalid",
            property,
            EnumParseFailureMode.SetNullAndLog,
            new InvalidOperationException("boom"),
            null
        };

        var originalLogger = TypeCoercionHelper.Logger;
        try
        {
            TypeCoercionHelper.Logger = NullLogger.Instance;
            var handled = (bool)method.Invoke(null, args)!;
            Assert.True(handled);
            Assert.Null(args[4]);
        }
        finally
        {
            TypeCoercionHelper.Logger = originalLogger;
        }
    }

    [Fact]
    public void CoerceValue_CoercesNumericValue()
    {
        var method = typeof(DataReaderMapper)
                         .GetMethod("CoerceValue", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new InvalidOperationException("CoerceValue method missing");

        var property = typeof(EnumEntity).GetProperty(nameof(EnumEntity.Color))!;
        var result = method.Invoke(null, new object?[]
        {
            2,
            property,
            typeof(long),
            EnumParseFailureMode.Throw
        });

        Assert.Equal(Enum.ToObject(typeof(Color), 2), result);
    }

    private enum Color
    {
        Red = 1,
        Blue = 2
    }

    private sealed class EnumEntity
    {
        public Color Color { get; set; }
        public Color? NullableColor { get; set; }
    }
}