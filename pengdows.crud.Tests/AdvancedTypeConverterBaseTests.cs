using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using Xunit;

namespace pengdows.crud.Tests;

public class AdvancedTypeConverterBaseTests
{
    private sealed class PassthroughConverter : AdvancedTypeConverter<int>
    {
        protected override object? ConvertToProvider(int value, SupportedDatabase provider)
        {
            return value * 2;
        }

        public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out int result)
        {
            if (value is int i)
            {
                result = i / 2;
                return true;
            }

            result = 0;
            return false;
        }
    }

    [Fact]
    public void ToProviderValue_ValidType_UsesOverride()
    {
        var converter = new PassthroughConverter();
        var result = converter.ToProviderValue(21, SupportedDatabase.Sqlite);

        Assert.Equal(42, result);
    }

    [Fact]
    public void ToProviderValue_InvalidType_Throws()
    {
        var converter = new PassthroughConverter();
        Assert.Throws<ArgumentException>(() => converter.ToProviderValue("bad", SupportedDatabase.Sqlite));
    }

    [Fact]
    public void FromProviderValue_DatabaseNull_ReturnsNull()
    {
        var converter = new PassthroughConverter();
        var result = converter.FromProviderValue(DBNull.Value, SupportedDatabase.Sqlite);

        Assert.Null(result);
    }

    [Fact]
    public void FromProviderValue_UsesOverride()
    {
        var converter = new PassthroughConverter();
        var result = converter.FromProviderValue(20, SupportedDatabase.Sqlite);

        Assert.Equal(10, result);
    }

    [Fact]
    public void TryConvertFromProvider_DefaultBehavior()
    {
        var converter = new PassthroughConverter();
        Assert.False(converter.TryConvertFromProvider("bad", SupportedDatabase.Sqlite, out _));
    }
}