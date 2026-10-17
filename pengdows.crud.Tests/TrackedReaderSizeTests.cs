using System;
using System.Collections.Generic;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Every read allocates one TrackedReader. 2.0.6 gave it about ten fields (the dialect's read-failure translator, its quirk
/// flags, the unreadable-value predicate, the unresolved-column dialect, the coercion options, the metrics timestamps and
/// the context locks); the A/B against 2.0.5 measured +56 B per read from them. The dialect-derived references are one
/// object shared by every reader of a dialect, so a reader holds one reference to them, not four.
/// </summary>
[Collection("AllocationSerial")]
public sealed class TrackedReaderSizeTests
{
    [Fact]
    public void ATrackedReaderObject_IsNoLargerThanTheCeiling()
    {
        var reader = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["id"] = 1 } });
        var connection = TestDoubles.Connection();
        var locker = TestDoubles.Disposable();
        // One profile, as a dialect hands every reader of it.
        var profile = new ReaderDialectProfile(null, null, null, TypeCoercionOptions.Default);
        TrackedReader Create() => new(reader, connection, locker, false, profile);
        for (var i = 0; i < 1_000; i++)
        {
            GC.KeepAlive(Create());
        }

        var bytes = AllocationMeasurement.Lowest(() =>
        {
            const int runs = 500;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < runs; i++)
            {
                GC.KeepAlive(Create());
            }

            return (GC.GetAllocatedBytesForCurrentThread() - before) / runs;
        });

        // 168 B with the four dialect-derived references held in the reader itself (2.0.6 before this change).
        Assert.True(bytes <= 144, $"A TrackedReader object is {bytes} B; expected at most 144 B.");
    }
}
