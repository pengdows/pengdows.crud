using System;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
using Xunit;

namespace pengdows.crud.Tests.exceptions;

// docs/planning/retry-context-design.md, shortcoming #5: PoolSaturatedException,
// ModeContentionException, and PoolForbiddenException are deliberately not DatabaseException
// subclasses and deliberately don't share a base class with each other (see CLAUDE.md's "Not part
// of this hierarchy" note) — merging them into one base type would risk SqlContainer's
// exception-translation path reclassifying one of them into CommandTimeoutException. The same
// IReadOnlyViolation marker-interface pattern already used for read-only rejections applies here:
// a shared empty marker interface lets RetryContext (and any other caller) detect "this was
// rejected by admission control before ever reaching the database" as one group, with zero change
// to what each exception actually extends.
public class ExecutionGovernanceRejectionTests
{
    [Fact]
    public void PoolSaturatedException_ImplementsMarkerInterface()
    {
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
        var ex = new PoolSaturatedException(PoolLabel.Writer, "abc123", snapshot, TimeSpan.FromMilliseconds(250));

        Assert.IsAssignableFrom<TimeoutException>(ex);
        Assert.IsAssignableFrom<IExecutionGovernanceRejection>(ex);
    }

    [Fact]
    public void ModeContentionException_ImplementsMarkerInterface()
    {
        var snapshot = new ModeContentionSnapshot(
            CurrentWaiters: 3,
            PeakWaiters: 5,
            TotalWaits: 9,
            TotalTimeouts: 2,
            TotalWaitTimeTicks: 42,
            AverageWaitTimeTicks: 7);
        var ex = new ModeContentionException(DbMode.SingleWriter, snapshot, TimeSpan.FromSeconds(4));

        Assert.IsAssignableFrom<TimeoutException>(ex);
        Assert.IsAssignableFrom<IExecutionGovernanceRejection>(ex);
    }

    [Fact]
    public void PoolForbiddenException_ImplementsMarkerInterface()
    {
        var ex = new PoolForbiddenException(PoolLabel.Writer, "abc123");

        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.IsAssignableFrom<IExecutionGovernanceRejection>(ex);
    }
}
