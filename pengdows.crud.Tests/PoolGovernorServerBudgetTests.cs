using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// POOL-003 phase 1. A governor in front of a shared per-server budget takes its local slot first and then a server
/// permit under the same acquire deadline, and returns both on release. Local lease, server permit and provider-opened
/// connection are three different counts; these pin the first two.
/// </summary>
public sealed class PoolGovernorServerBudgetTests
{
    private static readonly TimeSpan Patient = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);

    private static string Key() => "gov-" + Guid.NewGuid().ToString("N") + ":5432";

    private static (PoolGovernor Reader, PoolGovernor Writer, ServerBudgetRegistration ReaderReg,
        ServerBudgetRegistration WriterReg) Pair(int usable, int readerMax, int writerMax, TimeSpan timeout)
    {
        var key = Key();
        var readerReg = ServerBudgetRegistry.Register(key, usable, readerMax);
        var writerReg = ServerBudgetRegistry.Register(key, usable, writerMax);
        var reader = new PoolGovernor(PoolLabel.Reader, "r", readerMax, timeout, serverBudget: readerReg);
        var writer = new PoolGovernor(PoolLabel.Writer, "w", writerMax, timeout, serverBudget: writerReg);
        return (reader, writer, readerReg, writerReg);
    }

    [Fact]
    public void AReadHeavyWorkload_BorrowsTheWritersUnusedCapacity_UpToTheBudget()
    {
        var (reader, writer, rr, wr) = Pair(usable: 22, readerMax: 20, writerMax: 20, Patient);
        using var _ = reader; using var __ = writer; using var ___ = rr; using var ____ = wr;

        var held = new List<PoolSlot>();
        for (var i = 0; i < 20; i++)
        {
            held.Add(reader.Acquire()); // 20 readers: more than the static split's 11
        }

        Assert.Equal(20, rr.Budget.InUse);
        held.ForEach(s => s.Dispose());
        Assert.Equal(0, rr.Budget.InUse);
    }

    [Fact]
    public void WhenTheBudgetIsExhausted_TheNextCallerWaitsUnderTheAcquireTimeout_AndIsTurnedAway()
    {
        var (reader, writer, rr, wr) = Pair(usable: 22, readerMax: 20, writerMax: 20, Short);
        using var _ = reader; using var __ = writer; using var ___ = rr; using var ____ = wr;

        var held = new List<PoolSlot>();
        for (var i = 0; i < 20; i++) { held.Add(reader.Acquire()); }
        for (var i = 0; i < 2; i++) { held.Add(writer.Acquire()); } // 22: the whole budget

        Assert.Throws<PoolSaturatedException>(() => writer.Acquire());
        held.ForEach(s => s.Dispose());
    }

    [Fact]
    public void ATimedOutServerWait_GivesTheLocalSlotBack()
    {
        var (reader, writer, rr, wr) = Pair(usable: 2, readerMax: 5, writerMax: 5, Short);
        using var _ = reader; using var __ = writer; using var ___ = rr; using var ____ = wr;
        var w1 = writer.Acquire();
        var w2 = writer.Acquire(); // the budget of 2 is full

        Assert.Throws<PoolSaturatedException>(() => reader.Acquire());

        Assert.Equal(0, reader.GetSnapshot().InUse); // the reader's local slot was returned, not leaked
        w1.Dispose();
        using var later = reader.Acquire(); // and a permit freed by someone else is usable at once
        Assert.Equal(1, reader.GetSnapshot().InUse);
        w2.Dispose();
    }

    [Fact]
    public async Task ATimedOutAsyncServerWait_GivesTheLocalSlotBack()
    {
        var (reader, writer, rr, wr) = Pair(usable: 1, readerMax: 5, writerMax: 5, Short);
        using var _ = reader; using var __ = writer; using var ___ = rr; using var ____ = wr;
        var w1 = await writer.AcquireAsync();

        await Assert.ThrowsAsync<PoolSaturatedException>(async () => await reader.AcquireAsync());

        Assert.Equal(0, reader.GetSnapshot().InUse);
        w1.Dispose();
    }

    [Fact]
    public async Task AWaitingCaller_IsAdmittedWhenAnotherRoleReleasesAPermit()
    {
        var (reader, writer, rr, wr) = Pair(usable: 1, readerMax: 5, writerMax: 5, Patient);
        using var _ = reader; using var __ = writer; using var ___ = rr; using var ____ = wr;
        var w1 = writer.Acquire();

        var waiting = reader.AcquireAsync().AsTask();
        await Task.Delay(100);
        Assert.False(waiting.IsCompleted);

        w1.Dispose();
        using var admitted = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, rr.Budget.InUse);
    }

    [Fact]
    public void AMixedWorkload_NeverHoldsMoreThanTheBudget()
    {
        var (reader, writer, rr, wr) = Pair(usable: 6, readerMax: 20, writerMax: 20, Patient);
        using var _ = reader; using var __ = writer; using var ___ = rr; using var ____ = wr;
        var current = 0;
        var highest = 0;
        var callers = 0;

        // Dedicated threads, each holding its lease long enough to overlap: 24 callers against a budget of 6 would
        // reach well past 6 at once without a shared gate, so this cannot pass by luck of scheduling.
        var threads = new List<Thread>();
        for (var t = 0; t < 24; t++)
        {
            var governor = t % 2 == 0 ? reader : writer;
            var thread = new Thread(() =>
            {
                for (var n = 0; n < 5; n++)
                {
                    using var slot = governor.Acquire();
                    Interlocked.Increment(ref callers);
                    var now = Interlocked.Increment(ref current);
                    int seen;
                    while (now > (seen = Volatile.Read(ref highest)) &&
                           Interlocked.CompareExchange(ref highest, now, seen) != seen) { }
                    Thread.Sleep(5);
                    Interlocked.Decrement(ref current);
                }
            }) { IsBackground = true };
            threads.Add(thread);
        }

        threads.ForEach(t => t.Start());
        threads.ForEach(t => Assert.True(t.Join(TimeSpan.FromSeconds(60))));

        Assert.Equal(120, callers);
        Assert.True(highest <= 6, $"{highest} connections were held at once against a budget of 6.");
        Assert.True(highest >= 5, $"Only {highest} were ever held at once: the test did not load the budget.");
        Assert.Equal(0, rr.Budget.InUse);
    }

    [Fact]
    public void WhenTheBudgetIsDisengaged_NoServerPermitIsTaken()
    {
        var key = Key();
        using var registration = ServerBudgetRegistry.Register(key, usableLimit: 32767, configuredMaximum: 100);
        using var governor = new PoolGovernor(PoolLabel.Reader, "r", 100, Patient, serverBudget: registration);

        using var slot = governor.Acquire();

        Assert.False(registration.Budget.Engaged);
        Assert.Equal(0, registration.Budget.InUse);
    }

    // A lease taken before another context pushed the budget over its limit holds no server permit. Returning it
    // must not hand back a permit it never took, or the count would go below what is really in flight.
    [Fact]
    public void ALeaseTakenBeforeTheBudgetEngaged_DoesNotReleaseAPermitItNeverTook()
    {
        var key = Key();
        using var registration = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);
        using var governor = new PoolGovernor(PoolLabel.Reader, "r", 20, Patient, serverBudget: registration);
        var early = governor.Acquire();
        Assert.False(registration.Budget.Engaged);

        using var other = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);
        Assert.True(registration.Budget.Engaged);
        early.Dispose();

        Assert.Equal(0, registration.Budget.InUse); // not -1
    }

    // The same lease, if it is still held when the budget engages, is a real connection and must count from then on:
    // a sentinel taken at start-up is held for the whole life of the context.
    [Fact]
    public void ALeaseStillHeldWhenTheBudgetEngages_CountsAgainstIt()
    {
        var key = Key();
        using var registration = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);
        using var governor = new PoolGovernor(PoolLabel.Reader, "r", 20, Patient, serverBudget: registration);
        using var sentinel = governor.Acquire();

        using var other = ServerBudgetRegistry.Register(key, usableLimit: 22, configuredMaximum: 20);

        Assert.True(registration.Budget.Engaged);
        Assert.Equal(1, registration.Budget.InUse);
    }

    [Fact]
    public void ASentinelHeldFromTheStart_CountsAgainstAnEngagedBudget()
    {
        var (reader, writer, rr, wr) = Pair(usable: 22, readerMax: 20, writerMax: 20, Patient);
        using var _ = reader; using var __ = writer; using var ___ = rr; using var ____ = wr;

        using var readerSentinel = reader.Acquire();
        using var writerSentinel = writer.Acquire();

        Assert.Equal(2, rr.Budget.InUse);
    }

    // A writer transaction that opens a reader lease while it holds its own can wait on a budget that other
    // transactions hold. It must be turned away at the acquire timeout, never wait forever.
    [Fact]
    public async Task ANestedLease_WhenTheBudgetIsHeldByOthers_TimesOutInsteadOfDeadlocking()
    {
        var (reader, writer, rr, wr) = Pair(usable: 2, readerMax: 2, writerMax: 2, Short);
        using var _ = reader; using var __ = writer; using var ___ = rr; using var ____ = wr;
        var outerA = writer.Acquire();
        var outerB = writer.Acquire(); // two writer transactions hold the whole budget

        var nested = Task.Run(() => reader.Acquire()); // a transaction wanting a reader lease while holding its own

        var finished = await Task.WhenAny(nested, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(nested, finished);
        Assert.IsType<PoolSaturatedException>(nested.Exception!.GetBaseException());
        outerA.Dispose();
        outerB.Dispose();
    }
}
