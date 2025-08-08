#region

using System;
using testbed;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class StringAuditContextProviderTests
{
    [Fact]
    public void GetCurrentUserIdentifier_ReturnsExpectedUser()
    {
        // Arrange
        var provider = new StringAuditContextProvider();

        // Act
        var userId = provider.GetCurrentUserIdentifier();

        // Assert
        Assert.Equal("testuser", userId);
    }

    [Fact]
    public void GetUtcNow_ReturnsCurrentTimeInUtc()
    {
        // Arrange
        var provider = new StringAuditContextProvider();

        // Act
        var now = provider.Resolve().UtcNow;

        // Assert
        Assert.True(now.Kind == DateTimeKind.Utc);
        Assert.True((DateTime.UtcNow - now).TotalSeconds < 1);
    }
}