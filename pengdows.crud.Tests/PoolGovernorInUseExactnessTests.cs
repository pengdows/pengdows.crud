using System;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// <c>InUse</c> counts outstanding permit holders. It must not count a holder whose permit has already been returned,
/// even while that release is still finishing. Before the fix, <c>ReleaseToken</c> returned the permit before
/// decrementing <c>_inUse</c>, so a new holder taking the returned permit in that window was counted alongside the
/// old one (CONCURRENCY.md §5).
/// </summary>
public class PoolGovernorInUseExactnessTests
{
    [Fact]
    public void InUse_CountsOnlyOutstandingPermits_WhenAPermitIsReacquiredDuringRelease()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "inuse-exact", 1, TimeSpan.FromSeconds(5));
        var first = governor.Acquire();

        long inUseAtDecrement = -1;
        PoolSlot? second = null;

        // Runs inside the first release: its permit is already back in the gate, its holder-count decrement has not happened.
        governor.TestOnlyBeforeInUseDecrement = () =>
        {
            second = governor.Acquire();
            inUseAtDecrement = governor.GetSnapshot().InUse;
        };

        first.Dispose();

        // Exactly one permit is outstanding (the second holder). The old holder's permit is no longer held.
        Assert.Equal(1, inUseAtDecrement);

        governor.TestOnlyBeforeInUseDecrement = null;
        second!.Value.Dispose();
    }
}
