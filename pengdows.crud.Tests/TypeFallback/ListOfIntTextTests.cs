using System.Collections.Generic;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// A list of integers reads from its comma-separated text and from a JSON array (the form the library's other array
/// reads accept). Text that is neither declines (no value), rather than throwing.
/// </summary>
public class ListOfIntTextTests
{
    [Fact]
    public void AJsonArrayReadsAsTheList()
    {
        Assert.True(new ListOfIntCoercion().TryRead(new DbValue("[1,2,3]"), out var list));
        Assert.Equal(new List<int> { 1, 2, 3 }, list);
    }

    [Theory]
    [InlineData("1,x,3")]
    [InlineData("[1,")]
    public void MalformedTextDeclines(string text)
    {
        Assert.False(new ListOfIntCoercion().TryRead(new DbValue(text), out _));
    }
}
