using System;
using System.Diagnostics;
using pengdows.crud.enums;
using pengdows.crud.metrics;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class MetricsCollectorErrorCategoryTests
{
    [Fact]
    public void RecordDbError_Deadlock_IncrementsDeadlockCounter()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.RecordDbError(DbErrorCategory.Deadlock);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(1, snapshot.ErrorDeadlocks);
        Assert.Equal(0, snapshot.ErrorSerializationFailures);
        Assert.Equal(0, snapshot.ErrorConstraintViolations);
    }

    [Fact]
    public void RecordDbError_SerializationFailure_IncrementsSerializationCounter()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.RecordDbError(DbErrorCategory.SerializationFailure);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0, snapshot.ErrorDeadlocks);
        Assert.Equal(1, snapshot.ErrorSerializationFailures);
        Assert.Equal(0, snapshot.ErrorConstraintViolations);
    }

    [Fact]
    public void RecordDbError_ConstraintViolation_IncrementsConstraintCounter()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.RecordDbError(DbErrorCategory.ConstraintViolation);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0, snapshot.ErrorDeadlocks);
        Assert.Equal(0, snapshot.ErrorSerializationFailures);
        Assert.Equal(1, snapshot.ErrorConstraintViolations);
    }

    [Fact]
    public void RecordDbError_Unknown_DoesNotIncrementCategorizedCounters()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.RecordDbError(DbErrorCategory.Unknown);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0, snapshot.ErrorDeadlocks);
        Assert.Equal(0, snapshot.ErrorSerializationFailures);
        Assert.Equal(0, snapshot.ErrorConstraintViolations);
    }

    [Fact]
    public void RecordDbError_None_DoesNotIncrementAnything()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.RecordDbError(DbErrorCategory.None);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0, snapshot.ErrorDeadlocks);
        Assert.Equal(0, snapshot.ErrorSerializationFailures);
        Assert.Equal(0, snapshot.ErrorConstraintViolations);
    }

    [Fact]
    public void RecordDbError_Timeout_DoesNotIncrementCategorizedCounters()
    {
        // Timeout is already tracked via CommandTimedOut — RecordDbError(Timeout) is a no-op
        // for the categorized counters (deadlock/serialization/constraint)
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.RecordDbError(DbErrorCategory.Timeout);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0, snapshot.ErrorDeadlocks);
        Assert.Equal(0, snapshot.ErrorSerializationFailures);
        Assert.Equal(0, snapshot.ErrorConstraintViolations);
    }

    [Fact]
    public void RecordDbError_MultipleDeadlocks_AccumulatesCorrectly()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.RecordDbError(DbErrorCategory.Deadlock);
        collector.RecordDbError(DbErrorCategory.Deadlock);
        collector.RecordDbError(DbErrorCategory.SerializationFailure);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(2, snapshot.ErrorDeadlocks);
        Assert.Equal(1, snapshot.ErrorSerializationFailures);
    }

    [Fact]
    public void RecordDbError_ParentReceivesUpdates()
    {
        var parent = new MetricsCollector(MetricsOptions.Default);
        var child = new MetricsCollector(MetricsOptions.Default, parent);

        child.RecordDbError(DbErrorCategory.Deadlock);

        var parentSnapshot = parent.CreateSnapshot();
        Assert.Equal(1, parentSnapshot.ErrorDeadlocks);
    }
}
