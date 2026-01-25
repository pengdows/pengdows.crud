using System;
using pengdows.crud;
using Xunit;

namespace pengdows.crud.Tests;

public class TypeCoercionHelperAdditionalTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("False", false)]
    public void CoerceBoolean_FromString(string input, bool expected)
    {
        var raw = TypeCoercionHelper.Coerce(input, typeof(string), typeof(bool));
        Assert.NotNull(raw);
        var result = (bool)raw;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CoerceDateTime_FromString_IsUtc()
    {
        var raw = TypeCoercionHelper.Coerce("2020-01-01T00:00:00Z", typeof(string), typeof(DateTime));
        Assert.NotNull(raw);
        var result = (DateTime)raw;
        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void CoerceEnum_FromString()
    {
        var raw = TypeCoercionHelper.Coerce("Test2", typeof(string), typeof(NameEnum));
        Assert.NotNull(raw);
        var result = (NameEnum)raw;
        Assert.Equal(NameEnum.Test2, result);
    }

    [Fact]
    public void CoerceGuid_FromBytes()
    {
        var guid = Guid.NewGuid();
        var raw = TypeCoercionHelper.Coerce(guid.ToByteArray(), typeof(byte[]), typeof(Guid));
        Assert.NotNull(raw);
        var result = (Guid)raw;
        Assert.Equal(guid, result);
    }

    [Fact]
    public void GetJsonText_SerializesObject()
    {
        var payload = new { Value = 123 };
        var json = TypeCoercionHelper.GetJsonText(payload);
        Assert.Contains("\"Value\":123", json);
    }
}