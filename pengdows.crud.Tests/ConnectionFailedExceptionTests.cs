#region

using System;
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

    [Fact]
    public void Constructor_WithInnerException_SetsMessageAndInner()
    {
        // Arrange
        var message = "Failed to open database connection.";
        var inner = new InvalidOperationException("Connection refused");

        // Act
        var ex = new ConnectionFailedException(message, inner);

        // Assert
        Assert.Equal(message, ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void Phase_Property_CanBeSet()
    {
        // Act
        var ex = new ConnectionFailedException("test") { Phase = "InitConnect" };

        // Assert
        Assert.Equal("InitConnect", ex.Phase);
    }

    [Fact]
    public void Role_Property_CanBeSet()
    {
        // Act
        var ex = new ConnectionFailedException("test") { Role = "ReadWrite" };

        // Assert
        Assert.Equal("ReadWrite", ex.Role);
    }

    [Fact]
    public void Phase_And_Role_DefaultToNull()
    {
        // Act
        var ex = new ConnectionFailedException("test");

        // Assert
        Assert.Null(ex.Phase);
        Assert.Null(ex.Role);
    }

    [Fact]
    public void Constructor_WithInnerException_SupportsPhaseAndRole()
    {
        // Arrange
        var inner = new TimeoutException("timed out");

        // Act
        var ex = new ConnectionFailedException("Failed to validate read-only connection.", inner)
        {
            Phase = "ReadOnlyValidation",
            Role = "ReadOnly"
        };

        // Assert
        Assert.Equal("Failed to validate read-only connection.", ex.Message);
        Assert.Same(inner, ex.InnerException);
        Assert.Equal("ReadOnlyValidation", ex.Phase);
        Assert.Equal("ReadOnly", ex.Role);
    }
}