#region

using pengdows.crud.exceptions;
using Xunit;
using System.Threading.Tasks;

#endregion

namespace pengdows.crud.Tests;

public class NoColumnsFoundExceptionTests
{
    [Fact]
    public void Constructor_SetsMessageCorrectly()
    {
        // Arrange
        var message = "No columns were found for the entity.";

        // Act
        var ex = new NoColumnsFoundException(message);

        // Assert
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task CanBeThrownAndCaught()
    {
        // Arrange
        var message = "Something went wrong";

        // Act & Assert
        var thrown = await Record.ExceptionAsync(() => throw new NoColumnsFoundException(message));

        Assert.NotNull(thrown);
        Assert.IsType<NoColumnsFoundException>(thrown);
        Assert.Equal(message, thrown.Message);
    }
}
