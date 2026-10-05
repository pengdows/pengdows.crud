using System;
using System.Data;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REL-005: the context's connection-state handler built its Debug message
/// ("Opening connection: " + Name) on every open and close, with Debug logging off: about 4% of a
/// SQLite ReadSingle's allocations.
/// </summary>
[Collection("AllocationSerial")]
public sealed class ConnectionStateLoggingAllocationTests
{
    [Fact]
    public void StateChangeHandler_WithDebugLoggingOff_AllocatesNothing()
    {
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        var field = typeof(DatabaseContext).GetField("_stateChangeHandler", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var handler = (StateChangeEventHandler)field.GetValue(context)!;
        var open = new StateChangeEventArgs(ConnectionState.Closed, ConnectionState.Open);
        var close = new StateChangeEventArgs(ConnectionState.Open, ConnectionState.Closed);
        handler(this, open);
        handler(this, close);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            handler(this, open);
            handler(this, close);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
