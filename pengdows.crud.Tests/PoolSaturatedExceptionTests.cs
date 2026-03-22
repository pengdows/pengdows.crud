using System;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.metrics;
using Xunit;

namespace pengdows.crud.Tests;

public class PoolSaturatedExceptionTests
{
    [Fact]
    public void Constructor_SetsTimeoutProperty()
    {
        var timeout = TimeSpan.FromMilliseconds(250);
        var snapshot = new PoolStatisticsSnapshot(
            PoolLabel.Writer,
            "abc123",
            MaxSlots: 4,
            InUse: 3,
            PeakInUse: 3,
            Queued: 2,
            PeakQueued: 2,
            TurnstileQueued: 0,
            PeakTurnstileQueued: 0,
            TotalAcquired: 5,
            TotalWaitTicks: 0,
            TotalHoldTicks: 0,
            TotalSlotTimeouts: 1,
            TotalTurnstileTimeouts: 0,
            TotalCanceledWaits: 0,
            Disabled: false,
            Forbidden: false);
        var ex = new PoolSaturatedException(PoolLabel.Writer, "abc123", snapshot, timeout);

        Assert.Equal(timeout, ex.Timeout);
    }
}
