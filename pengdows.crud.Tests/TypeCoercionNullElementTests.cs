using System;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

// A NULL element cannot go into a non-nullable element type. It must fail as a conversion failure
// (InvalidCastException), which hydration reports as DataMappingException, not as an
// ArgumentNullException that escapes the gateway's conversion-failure handling.
public class TypeCoercionNullElementTests
{
    [Fact]
    public void NullElement_IntoNonNullableArray_FailsAsConversionFailure()
    {
        var source = new object?[] { 1, DBNull.Value, 3 };

        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce(source, typeof(object[]), typeof(int[])));
    }

    [Fact]
    public void NullElement_IntoNullableArray_StaysNull()
    {
        var source = new object?[] { 1, DBNull.Value, 3 };

        var result = (int?[])TypeCoercionHelper.Coerce(source, typeof(object[]), typeof(int?[]))!;

        Assert.Equal(new int?[] { 1, null, 3 }, result);
    }
}
