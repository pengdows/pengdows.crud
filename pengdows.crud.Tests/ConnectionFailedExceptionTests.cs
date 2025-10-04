#region

using System.Threading.Tasks;
using pengdows.crud.exceptions;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ConnectionFailedExceptionTests
{
    [Fact]
    public void Constructor_SetsMessageCorrectly()
    {
        // Arrange
        var message = "Unable to connect to the database";

        // Act
        var ex = new ConnectionFailedException(message);

        // Assert
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task CanBeThrownAndCaught()
    {
        // Arrange
        var message = "Failed to connect";

        // Act & Assert
        var thrown = await Record.ExceptionAsync(() => throw new ConnectionFailedException(message));

        Assert.NotNull(thrown);
        Assert.IsType<ConnectionFailedException>(thrown);
        Assert.Equal(message, thrown.Message);
    }
}
