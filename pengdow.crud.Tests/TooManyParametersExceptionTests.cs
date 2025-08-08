#region

using System.Threading.Tasks;
using pengdow.crud.exceptions;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

public class TooManyParametersExceptionTests
{
    [Fact]
    public void Constructor_SetsMessageCorrectly()
    {
        // Arrange
        var message = "Too many parameters were found";

        // Act
        var ex = new TooManyParametersException(message, 10000);

        // Assert
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task CanBeThrownAndCaught()
    {
        // Arrange
        var message = "Too Many parameters for this database were found";

        // Act & Assert
        var thrown = await Record.ExceptionAsync(() => throw new TooManyParametersException(message, 10000));

        Assert.NotNull(thrown);
        Assert.IsType<TooManyParametersException>(thrown);
        Assert.Equal(message, thrown.Message);
    }
}