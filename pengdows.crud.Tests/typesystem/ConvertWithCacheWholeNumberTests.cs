using System;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Review finding (Ring 1, latent): the compiled Convert.ChangeType route rounded a fractional number into an integer
/// (2.7 → 3), where the whole-number guard on the other routes refuses it (COR-009). The route must refuse it too.
/// </summary>
public class ConvertWithCacheWholeNumberTests
{
    [Theory]
    [InlineData(2.7d, typeof(int))]
    [InlineData(2.5d, typeof(long))]
    [InlineData(2.7f, typeof(int))]
    public void ConvertWithCache_FractionalNumberIntoInteger_Throws(object value, Type target)
    {
        Assert.Throws<InvalidCastException>(() => ConverterResolution.ConvertWithCache(value, target));
    }

    [Fact]
    public void ConvertWithCache_WholeNumberIntoInteger_Converts()
    {
        Assert.Equal(4, ConverterResolution.ConvertWithCache(4.0d, typeof(int)));
    }
}
