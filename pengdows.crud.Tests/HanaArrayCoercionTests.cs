using System;
using System.Linq;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// HanaArrayCoercion's edge and error paths (TYPE-020): what Sap.Data.Hana.Net returns for an ARRAY
/// column is decoded exactly or not at all. A value that doesn't decode as the property's element type
/// throws rather than producing a wrong array. The happy paths run through the gateway in
/// <c>HanaArrayTests</c>.
/// </summary>
public class HanaArrayCoercionTests
{
    private static readonly CoercionRegistry Registry = Build();

    private static CoercionRegistry Build()
    {
        var registry = new CoercionRegistry();
        HanaArrayCoercion.RegisterAll(registry, SupportedDatabase.SapHana);
        return registry;
    }

    private static object? Read(Type target, object raw)
    {
        Assert.True(Registry.TryRead(new DbValue(raw), target, out var value, SupportedDatabase.SapHana));
        return value;
    }

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    [Theory]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(int?[]))]
    [InlineData(typeof(string[]))]
    public void NonBinaryValue_IsNotRead(Type target)
    {
        Assert.False(Registry.TryRead(new DbValue("[1,2]"), target, out _, SupportedDatabase.SapHana));
    }

    [Theory]
    [InlineData("FFFFFF7F")]          // count far beyond the bytes
    [InlineData("000000")]            // shorter than the count itself
    public void NotAnArray_Throws(string hex)
    {
        Assert.Throws<FormatException>(() => Read(typeof(int[]), Hex(hex)));
    }

    [Theory]
    [InlineData("010000000501000000")] // a null indicator that is neither 00 nor 01
    [InlineData("01000000010100")]     // the value is cut short
    [InlineData("0200000001010000000102000000FF")] // a byte left over
    public void MalformedFixedWidth_Throws(string hex)
    {
        Assert.Throws<FormatException>(() => Read(typeof(int?[]), Hex(hex)));
    }

    [Fact]
    public void LongerString_UsesTheFourByteLength()
    {
        var text = new string('y', 70000);
        var bytes = Hex("01000000F7").Concat(BitConverter.GetBytes(70000)).Concat(Enumerable.Repeat((byte)'y', 70000)).ToArray();

        Assert.Equal(new[] { text }, Read(typeof(string[]), bytes));
    }

    [Theory]
    [InlineData("020000000161")]     // the second element is missing
    [InlineData("01000000F861")]     // an unknown length indicator
    [InlineData("010000000561")]     // the length runs past the end
    [InlineData("01000000016162")]   // a byte left over
    public void MalformedStrings_Throw(string hex)
    {
        Assert.Throws<FormatException>(() => Read(typeof(string[]), Hex(hex)));
    }

    // HANA sends CESU-8, but plain 4-byte UTF-8 decodes too; anything else is not text.
    [Fact]
    public void FourByteUtf8_Decodes_InvalidBytes_Throw()
    {
        Assert.Equal(new[] { "😀" }, Read(typeof(string[]), Hex("0100000004F09F9880")));
        Assert.Throws<FormatException>(() => Read(typeof(string[]), Hex("010000000180")));
    }

    [Fact]
    public void Write_PassesTheArrayThrough_NullAsDBNull()
    {
        var parameter = new fakeDbParameter();
        foreach (var (type, value) in new (Type, object?)[]
                 {
                     (typeof(int[]), new[] { 1 }), (typeof(int?[]), new int?[] { 1, null }), (typeof(string[]), new[] { "a" })
                 })
        {
            var coercion = Registry.GetCoercion(type, SupportedDatabase.SapHana)!;
            Assert.True(coercion.TryWrite(value, parameter));
            Assert.Same(value, parameter.Value);
            Assert.True(coercion.TryWrite(null, parameter));
            Assert.Equal(DBNull.Value, parameter.Value);
        }
    }
}
