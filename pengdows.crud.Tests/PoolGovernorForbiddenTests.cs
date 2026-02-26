// =============================================================================
// FILE: PoolGovernorForbiddenTests.cs
// PURPOSE: Tests for PoolGovernor forbidden state (MaxSlots=0 explicitly).
//
// A forbidden governor differs from a disabled governor:
//   - Disabled: returns default slot (no contention management needed)
//   - Forbidden: throws PoolForbiddenException (operation is not permitted)
// =============================================================================

using System;
using System.Threading.Tasks;
using pengdows.crud.exceptions;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolGovernorForbiddenTests
{
    private static PoolGovernor MakeForbidden(PoolLabel label = PoolLabel.Writer, string key = "writer-key")
        => new(label, key, 0, TimeSpan.FromMilliseconds(100), forbidden: true);

    // -------------------------------------------------------------------------
    // Acquire (sync)
    // -------------------------------------------------------------------------

    [Fact]
    public void Acquire_WhenForbidden_ThrowsPoolForbiddenException()
    {
        var governor = MakeForbidden();

        var ex = Assert.Throws<PoolForbiddenException>(() => governor.Acquire());

        Assert.Equal(PoolLabel.Writer, ex.PoolLabel);
        Assert.Equal("writer-key", ex.PoolKeyHash);
    }

    // -------------------------------------------------------------------------
    // AcquireAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AcquireAsync_WhenForbidden_ThrowsPoolForbiddenException()
    {
        var governor = MakeForbidden();

        var ex = await Assert.ThrowsAsync<PoolForbiddenException>(
            async () => await governor.AcquireAsync());

        Assert.Equal(PoolLabel.Writer, ex.PoolLabel);
        Assert.Equal("writer-key", ex.PoolKeyHash);
    }

    // -------------------------------------------------------------------------
    // TryAcquire (sync)
    // -------------------------------------------------------------------------

    [Fact]
    public void TryAcquire_WhenForbidden_ThrowsPoolForbiddenException()
    {
        var governor = MakeForbidden();

        Assert.Throws<PoolForbiddenException>(() => governor.TryAcquire(out _));
    }

    // -------------------------------------------------------------------------
    // TryAcquireAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryAcquireAsync_WhenForbidden_ThrowsPoolForbiddenException()
    {
        var governor = MakeForbidden();

        await Assert.ThrowsAsync<PoolForbiddenException>(
            async () => await governor.TryAcquireAsync());
    }

    // -------------------------------------------------------------------------
    // WaitForDrainAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WaitForDrainAsync_WhenForbidden_CompletesImmediately()
    {
        var governor = MakeForbidden();

        // Should not block or throw — nothing to drain in a forbidden pool
        await governor.WaitForDrainAsync(TimeSpan.FromMilliseconds(50));
    }

    // -------------------------------------------------------------------------
    // GetSnapshot
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSnapshot_WhenForbidden_ReportsForbiddenTrue()
    {
        var governor = MakeForbidden(PoolLabel.Reader, "read-key");

        var snapshot = governor.GetSnapshot();

        Assert.True(snapshot.Forbidden);
        Assert.False(snapshot.Disabled);
        Assert.Equal(PoolLabel.Reader, snapshot.Label);
        Assert.Equal("read-key", snapshot.PoolKeyHash);
        Assert.Equal(0, snapshot.MaxSlots);
    }

    // -------------------------------------------------------------------------
    // Disabled governor regression — must NOT throw
    // -------------------------------------------------------------------------

    [Fact]
    public void Acquire_WhenDisabled_ReturnsDefaultSlot()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 0,
            TimeSpan.FromMilliseconds(100), disabled: true);

        // Should not throw — disabled governors are used for SingleConnection mode
        var slot = governor.Acquire();
        Assert.Equal(default, slot);
    }

    [Fact]
    public async Task AcquireAsync_WhenDisabled_ReturnsDefaultSlot()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 0,
            TimeSpan.FromMilliseconds(100), disabled: true);

        var slot = await governor.AcquireAsync();
        Assert.Equal(default, slot);
    }

    // -------------------------------------------------------------------------
    // Snapshot Disabled vs Forbidden distinction
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSnapshot_WhenDisabled_ReportsDisabledTrue_ForbiddenFalse()
    {
        var governor = new PoolGovernor(PoolLabel.Writer, "w", 0,
            TimeSpan.FromMilliseconds(100), disabled: true);

        var snapshot = governor.GetSnapshot();

        Assert.True(snapshot.Disabled);
        Assert.False(snapshot.Forbidden);
    }

    // -------------------------------------------------------------------------
    // Exception message content
    // -------------------------------------------------------------------------

    [Fact]
    public void PoolForbiddenException_Message_ContainsLabelAndKey()
    {
        var ex = new PoolForbiddenException(PoolLabel.Writer, "pool-hash-abc");

        Assert.Contains("Writer", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pool-hash-abc", ex.Message);
    }
}
