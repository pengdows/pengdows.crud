using System;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Review finding (Ring 1): TryCoerceWideInteger documents that a value which is not a whole number declines, so the
/// general conversion can take it. Text such as "1.5" threw FormatException from BigInteger.Parse instead.
/// </summary>
public class WideIntegerTextDeclineTests
{
    [Theory]
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData("")]
    public void TryCoerceWideInteger_TextThatIsNotAWholeNumber_Declines(string text)
    {
        var declined = WideIntegerCoercion.TryCoerceWideInteger(text, typeof(Int128), out var result);

        Assert.False(declined);
        Assert.Null(result);
    }

    [Fact]
    public void TryCoerceWideInteger_WholeNumberText_IsConverted()
    {
        Assert.True(WideIntegerCoercion.TryCoerceWideInteger("12", typeof(Int128), out var result));
        Assert.Equal((Int128)12, result);
    }
}
