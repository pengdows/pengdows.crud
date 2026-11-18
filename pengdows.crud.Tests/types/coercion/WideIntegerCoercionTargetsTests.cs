using System;
using System.Numerics;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.WideIntegerTargets
{
    public class WideIntegerCoercionTargetsTests
    {
        [Theory]
        [InlineData(typeof(sbyte), (sbyte)5)]
        [InlineData(typeof(byte), (byte)5)]
        [InlineData(typeof(short), (short)5)]
        [InlineData(typeof(ushort), (ushort)5)]
        [InlineData(typeof(uint), (uint)5)]
        public void TryCoerce_BigIntegerToNarrowIntegerTarget_ConvertsTheValue(Type target, object expected)
        {
            Assert.True(WideIntegerCoercion.TryCoerceWideInteger(new BigInteger(5), target, out var result));
            Assert.Equal(expected, result);
        }

        [Fact]
        public void TryCoerce_WideValueToUnsupportedTarget_ReturnsFalse()
        {
            Assert.False(WideIntegerCoercion.TryCoerceWideInteger(new BigInteger(5), typeof(Guid), out var result));
            Assert.Null(result);
        }
    }
}
