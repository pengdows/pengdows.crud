using System;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The pool size a context may use is the smaller of what the caller asked for and what the server is
/// configured to allow. The provider default (100) applies only when the caller asked for nothing; an
/// explicit request is never capped by it.
/// </summary>
public sealed class ConnectionCeilingTests
{
    private const int ProviderDefault = 100;

    [Fact]
    public void RequestedBelowEveryLimit_IsUsedAsIs()
    {
        var r = ConnectionCeiling.Resolve(requested: 20, serverConfigured: 25, ProviderDefault);

        Assert.Equal(20, r.Value);
        Assert.False(r.WasClamped);
    }

    [Fact]
    public void ServerConfiguredBelowRequested_ClampsToTheServer()
    {
        var r = ConnectionCeiling.Resolve(requested: 100, serverConfigured: 25, ProviderDefault);

        Assert.Equal(25, r.Value);
        Assert.True(r.WasClamped);
        Assert.Equal(ConnectionCeilingLimit.ServerConfigured, r.Limiter);
    }

    [Fact]
    public void NothingRequested_UsesTheProviderDefault_WhenTheServerAllowsIt()
    {
        var r = ConnectionCeiling.Resolve(requested: null, serverConfigured: 500, ProviderDefault);

        Assert.Equal(100, r.Value);
        Assert.False(r.WasClamped);
        Assert.Equal(ConnectionCeilingLimit.ProviderDefault, r.Limiter);
    }

    [Fact]
    public void NothingRequested_AndASmallServer_ClampsTheProviderDefaultToTheServer()
    {
        // The exact case that failed in the storm: default pool 100 against max_connections=25.
        var r = ConnectionCeiling.Resolve(requested: null, serverConfigured: 25, ProviderDefault);

        Assert.Equal(25, r.Value);
        Assert.True(r.WasClamped);
        Assert.Equal(ConnectionCeilingLimit.ServerConfigured, r.Limiter);
    }

    [Fact]
    public void AnExplicitRequestAboveTheProviderDefault_IsNotCappedByIt()
    {
        var r = ConnectionCeiling.Resolve(requested: 200, serverConfigured: null, ProviderDefault);

        Assert.Equal(200, r.Value);
        Assert.False(r.WasClamped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveServerValue_MeansUnknownOrUnlimited_AndIsIgnored(int probed)
    {
        // SQL Server reports 0 for "user connections" when it is unlimited.
        var r = ConnectionCeiling.Resolve(requested: 50, serverConfigured: probed, ProviderDefault);

        Assert.Equal(50, r.Value);
        Assert.False(r.WasClamped);
    }

    [Fact]
    public void RequestEqualToTheServerLimit_IsNotReportedAsClamped()
    {
        var r = ConnectionCeiling.Resolve(requested: 25, serverConfigured: 25, ProviderDefault);

        Assert.Equal(25, r.Value);
        Assert.False(r.WasClamped);
    }

    // ── configured headroom: slots the application asks to leave free on the server for other
    //    clients (monitoring, DBAs, other apps). It reserves server capacity, so it comes off the
    //    server's usable limit, not off a request that already fits. ──

    [Fact]
    public void Headroom_IsTakenOffTheServersUsableLimit()
    {
        var r = ConnectionCeiling.Resolve(requested: null, serverConfigured: 25, ProviderDefault, headroom: 5);

        Assert.Equal(20, r.Value);
        Assert.Equal(ConnectionCeilingLimit.ServerConfigured, r.Limiter);
        Assert.True(r.HeadroomApplied);
    }

    [Fact]
    public void Headroom_DoesNotShrinkARequestThatAlreadyFitsWithinWhatIsLeft()
    {
        var r = ConnectionCeiling.Resolve(requested: 10, serverConfigured: 25, ProviderDefault, headroom: 5);

        Assert.Equal(10, r.Value);
        Assert.False(r.WasClamped);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(40)]
    public void HeadroomThatConsumesTheWholeServerLimit_IsRejected_NotSilentlyTurnedIntoOne(int headroom)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectionCeiling.Resolve(requested: null, serverConfigured: 25, ProviderDefault, headroom));
    }

    [Fact]
    public void NegativeHeadroom_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectionCeiling.Resolve(requested: 10, serverConfigured: 25, ProviderDefault, headroom: -1));
    }

    [Fact]
    public void Headroom_WithAnUnknownServerLimit_HasNothingToReserveFrom_AndSaysSo()
    {
        var r = ConnectionCeiling.Resolve(requested: 50, serverConfigured: null, ProviderDefault, headroom: 5);

        Assert.Equal(50, r.Value);
        Assert.False(r.HeadroomApplied);
    }

    [Fact]
    public void ZeroHeadroom_ChangesNothing()
    {
        var r = ConnectionCeiling.Resolve(requested: null, serverConfigured: 25, ProviderDefault, headroom: 0);

        Assert.Equal(25, r.Value);
        Assert.False(r.HeadroomApplied);
    }
}
