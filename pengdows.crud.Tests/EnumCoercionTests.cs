using System;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests
{
    public enum CoercionColor { Red = 1, Green = 2 }

    /// <summary>
    /// The enum conversion is its own unit: the target's enum type, the parse with its failure modes, and the default a
    /// failed read takes. It is called directly, not through the general coercion.
    /// </summary>
    public class EnumCoercionTests
    {
        [Fact]
        public void TheEnumTypeOfATargetIsFoundIncludingItsNullableForm()
        {
            Assert.True(EnumCoercion.TryGetEnumType(typeof(CoercionColor), out var plain));
            Assert.Equal(typeof(CoercionColor), plain);
            Assert.True(EnumCoercion.TryGetEnumType(typeof(CoercionColor?), out var nullable));
            Assert.Equal(typeof(CoercionColor), nullable);
            Assert.False(EnumCoercion.TryGetEnumType(typeof(int), out _));
        }

        [Fact]
        public void AStringParsesToTheMemberIgnoringCase()
        {
            var result = EnumCoercion.CoerceEnum("green", typeof(CoercionColor), EnumParseFailureMode.Throw, typeof(CoercionColor));

            Assert.Equal(CoercionColor.Green, result);
        }

        [Fact]
        public void AnUndefinedStringThrowsInThrowMode()
        {
            Assert.ThrowsAny<Exception>(() =>
                EnumCoercion.CoerceEnum("blue", typeof(CoercionColor), EnumParseFailureMode.Throw, typeof(CoercionColor)));
        }

        [Fact]
        public void AnUndefinedStringIsTheDefaultInDefaultMode()
        {
            var result = EnumCoercion.CoerceEnum("blue", typeof(CoercionColor), EnumParseFailureMode.SetDefaultValue, typeof(CoercionColor));

            Assert.Equal(default(CoercionColor), result);
        }

        [Fact]
        public void ANumberIsTheMemberWithThatValue()
        {
            var result = EnumCoercion.CoerceEnum(2L, typeof(CoercionColor), EnumParseFailureMode.Throw, typeof(CoercionColor));

            Assert.Equal(CoercionColor.Green, result);
        }
    }
}
