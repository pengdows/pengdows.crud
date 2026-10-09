using System;
using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The server-ceiling clamp is opt-in on the 2.0.x line (it can silently shrink a pool that exceeds
/// the server's limit), and headroom defaults to 0: a library should not invent a reservation the
/// application did not ask for.
/// </summary>
public sealed class ServerCeilingConfigurationTests
{
    [Fact]
    public void Defaults_AreOff_AndZeroHeadroom()
    {
        var config = new DatabaseContextConfiguration();

        Assert.False(config.ClampPoolsToServerConnectionLimit);
        Assert.Equal(0, config.ResourceConnectionHeadroom);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void NegativeHeadroom_IsRejected(int headroom)
    {
        var config = new DatabaseContextConfiguration();

        Assert.Throws<ArgumentOutOfRangeException>(() => config.ResourceConnectionHeadroom = headroom);
    }

    [Fact]
    public void TenantCloning_KeepsBothSettings_SoAPerTenantContextDoesNotSilentlyLoseTheClamp()
    {
        var source = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=db1",
            ClampPoolsToServerConnectionLimit = true,
            ResourceConnectionHeadroom = 5
        };

        var clone = (DatabaseContextConfiguration)typeof(TenantConnectionResolver)
            .GetMethod("CloneConfiguration", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { source })!;

        Assert.True(clone.ClampPoolsToServerConnectionLimit);
        Assert.Equal(5, clone.ResourceConnectionHeadroom);
    }
}
