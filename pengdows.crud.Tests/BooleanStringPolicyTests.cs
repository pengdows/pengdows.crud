using System;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

// Boolean text: bool.TryParse decides true and false. The explicit false tokens "0", "0.0", "f",
// "n", "no", "NaN" and "-NaN" (any case) are false. Anything else, including "truue", "yes", "1",
// and blank text, fails the read rather than guessing a value.
public class BooleanStringPolicyTests
{
    [Theory]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData(" false ")]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("f")]
    [InlineData("F")]
    [InlineData("n")]
    [InlineData("N")]
    [InlineData("no")]
    [InlineData("NO")]
    [InlineData("NaN")]
    [InlineData("-NaN")]
    public void FalseTokens_ReadAsFalse(string text)
    {
        Assert.False((bool)TypeCoercionHelper.Coerce(text, typeof(string), typeof(bool))!);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" True ")]
    [InlineData("Y")]
    [InlineData("y")]
    public void TrueTokens_ReadAsTrue(string text)
    {
        Assert.True((bool)TypeCoercionHelper.Coerce(text, typeof(string), typeof(bool))!);
    }

    // Whitespace around a token is not part of it, whichever token it is.
    [Theory]
    [InlineData("\ttrue\n", true)]
    [InlineData(" y ", true)]
    [InlineData("\tno ", false)]
    [InlineData(" FALSE\r\n", false)]
    [InlineData("\t0.0 ", false)]
    public void SurroundingWhitespace_IsIgnored(string text, bool expected)
    {
        Assert.Equal(expected, (bool)TypeCoercionHelper.Coerce(text, typeof(string), typeof(bool))!);
    }

    [Theory]
    // Control characters must never fold onto a token character (0x0E | 0x20 == '.', 0x10 | 0x20 == '0').
    // Leading and trailing whitespace is trimmed, so these cases put the character inside the token.
    [InlineData("0\u000E0")]
    [InlineData("\u0010.0")]
    [InlineData("truue")]
    [InlineData("tru")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("t")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("maybe")]
    [InlineData("abc")]
    public void UnrecognisedText_FailsTheRead(string text)
    {
        var read = CoercionRegistry.Shared.TryRead(new DbValue(text, typeof(string)), typeof(bool), out var value);

        Assert.False(read);
        Assert.False(value is bool b && b);
        Assert.ThrowsAny<Exception>(() => TypeCoercionHelper.Coerce(text, typeof(string), typeof(bool)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankText_FailsTheRead(string text)
    {
        Assert.Throws<FormatException>(() => TypeCoercionHelper.Coerce(text, typeof(string), typeof(bool)));
    }

    [Theory]
    [InlineData('0', false)]
    [InlineData('f', false)]
    [InlineData('n', false)]
    [InlineData('N', false)]
    [InlineData('F', false)]
    [InlineData('Y', true)]
    [InlineData('y', true)]
    public void SingleCharacterFalseTokens_ReadAsFalse(char c, bool expected)
    {
        Assert.Equal(expected, TypeCoercionHelper.Coerce(c, typeof(char), typeof(bool)));
    }

    [Theory]
    [InlineData('x')]
    [InlineData('t')]
    [InlineData('1')]
    public void SingleCharacterUnrecognised_FailsTheRead(char c)
    {
        Assert.ThrowsAny<Exception>(() => TypeCoercionHelper.Coerce(c, typeof(char), typeof(bool)));
    }
}
