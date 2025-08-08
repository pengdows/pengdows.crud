#region

using pengdow.crud.exceptions;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

public class ExceptionTests
{
    [Fact]
    public void TooManyParametersException_CarriesLimit()
    {
        var ex = new TooManyParametersException("Exceeded", 2000);
        Assert.Equal("Exceeded", ex.Message);
        Assert.Equal(2000, ex.MaxAllowed);
    }
}