using System;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolGovernorSharedGateOwnershipTests
{
    [Fact]
    public void Gate_StaysUsableUntilEveryOwnerHasDisposedIt()
    {
        var gate = new PoolGovernorConcurrencyGate(2);
        gate.AddOwner();

        gate.Dispose();
        Assert.True(gate.TryAcquire(out var permit));
        gate.Release(permit);

        gate.Dispose();
        Assert.Throws<ObjectDisposedException>(() => gate.TryAcquire(out _));
    }

    [Fact]
    public void DisposingOneGovernor_DoesNotBreakTheOtherGovernorSharingTheGate()
    {
        var shared = new PoolGovernorConcurrencyGate(4, int.MaxValue);
        var writer = CreateGovernor(PoolLabel.Writer, shared);
        var reader = CreateGovernor(PoolLabel.Reader, shared);
        shared.Dispose(); // the creator's reference

        writer.Dispose();

        using var slot = reader.Acquire();
        Assert.Equal(1, shared.ActiveCount);
    }

    [Fact]
    public void TheGateIsDisposedOnceTheLastSharingGovernorIsDisposed()
    {
        var shared = new PoolGovernorConcurrencyGate(4, int.MaxValue);
        var writer = CreateGovernor(PoolLabel.Writer, shared);
        var reader = CreateGovernor(PoolLabel.Reader, shared);
        shared.Dispose();

        writer.Dispose();
        reader.Dispose();

        Assert.Throws<ObjectDisposedException>(() => shared.TryAcquire(out _));
    }

    private static PoolGovernor CreateGovernor(PoolLabel label, PoolGovernorConcurrencyGate shared) =>
        new(label, "ownership-test", 2, TimeSpan.FromSeconds(1), sharedConcurrencyGate: shared);
}
