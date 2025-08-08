#region

using pengdow.crud.exceptions;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

public class TooManyColumnsTests
{
    [Fact]
    public void Constructor_SetsMessageCorrectly()
    {
        // Arrange
        var message = "Too many columns were found";

        // Act
        var ex = new TooManyColumns(message);

        // Assert
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async void CanBeThrownAndCaught()
    {
        // Arrange
        var message = "Too many columns were found";

        // Act & Assert
        var thrown = await Record.ExceptionAsync(() => throw new TooManyColumns(message));

        Assert.NotNull(thrown);
        Assert.IsType<TooManyColumns>(thrown);
        Assert.Equal(message, thrown.Message);
    }
}