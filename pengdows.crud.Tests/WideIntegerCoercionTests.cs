using System;
using System.Numerics;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests
{
    /// <summary>
    /// The 128-bit and arbitrary-size integers convert exactly, to and from the integer types, decimal, double and text. A
    /// value that is not a whole number declines (the general path converts it).
    /// </summary>
    public class WideIntegerCoercionTests
    {
        [Fact]
        public void ABigIntegerConvertsToAnIntegerTypeWhenItFits()
        {
            Assert.True(WideIntegerCoercion.TryCoerceWideInteger(new BigInteger(7), typeof(long), out var result));

            Assert.Equal(7L, result);
        }

        [Fact]
        public void TextOfAWideIntegerParsesExactly()
        {
            Assert.True(WideIntegerCoercion.TryCoerceWideInteger("123456789012345678901234567890", typeof(BigInteger), out var result));

            Assert.Equal(BigInteger.Parse("123456789012345678901234567890"), result);
        }

        [Fact]
        public void AWholeDecimalConvertsAndAFractionDeclines()
        {
            Assert.True(WideIntegerCoercion.TryCoerceWideInteger(3m, typeof(Int128), out var whole));
            Assert.Equal((Int128)3, whole);

            Assert.False(WideIntegerCoercion.TryCoerceWideInteger(2.5m, typeof(Int128), out _));
        }

        [Fact]
        public void AnInt128ConvertsToAnArbitrarySizeInteger()
        {
            var value = Int128.MaxValue;

            Assert.True(WideIntegerCoercion.TryCoerceWideInteger(value, typeof(BigInteger), out var result));

            Assert.Equal(BigInteger.Parse(Int128.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)), result);
        }

        [Fact]
        public void ANonIntegerTextThrows()
        {
            Assert.Throws<FormatException>(() => WideIntegerCoercion.TryCoerceWideInteger("not-a-number", typeof(BigInteger), out _));
        }

        [Fact]
        public void AValueWithNoWideIntegerEitherWayDeclines()
        {
            Assert.False(WideIntegerCoercion.TryCoerceWideInteger(1.5, typeof(string), out _));
        }
    }
}
