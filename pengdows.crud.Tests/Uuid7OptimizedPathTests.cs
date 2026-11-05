using System;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("Uuid7StaticStateSerial")]
public sealed class Uuid7OptimizedPathTests
{
    private static readonly BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo ThreadStateField =
        typeof(Uuid7Optimized).GetField("_threadState", NonPublicStatic)!;
    private static readonly FieldInfo GlobalEpochField =
        typeof(Uuid7Optimized).GetField("_globalEpochMs", NonPublicStatic)!;
    private static readonly FieldInfo OptionsField =
        typeof(Uuid7Optimized).GetField("_opts", NonPublicStatic)!;
    private static readonly MethodInfo ReseedRandomStateMethod =
        typeof(Uuid7Optimized).GetMethod("ReseedRandomState", NonPublicStatic)!;

    private static readonly Type TlsType =
        typeof(Uuid7Optimized).GetNestedType("V7ThreadState", BindingFlags.NonPublic)!;
    private static readonly FieldInfo LastMsField = TlsType.GetField("LastMs")!;
    private static readonly FieldInfo CounterField = TlsType.GetField("Counter")!;
    private static readonly FieldInfo RandAField = TlsType.GetField("RandA")!;
    private static readonly FieldInfo RandBField = TlsType.GetField("RandB")!;
    private static readonly FieldInfo RandomIndexField = TlsType.GetField("RandomIndex")!;
    private static readonly FieldInfo RandomBufferField = TlsType.GetField("RandomBuffer")!;

    private static readonly ushort RandAMask =
        Convert.ToUInt16(typeof(Uuid7Optimized).GetField("RandAMask", NonPublicStatic)!.GetRawConstantValue()!);
    private static readonly ulong RandBMask =
        Convert.ToUInt64(typeof(Uuid7Optimized).GetField("RandBMask", NonPublicStatic)!.GetRawConstantValue()!);

    private readonly record struct StateSnapshot(
        long LastMs,
        int Counter,
        ushort RandA,
        ulong RandB,
        int RandomIndex,
        long GlobalEpoch,
        Uuid7Options Options);

    [Fact]
    public void TryNewUuid7_WhenEnteringNewMillisecond_ResetsCounterAndUpdatesEpoch()
    {
        var state = GetCurrentThreadState();
        var snapshot = Capture(state);
        try
        {
            LastMsField.SetValue(state, 0L);
            CounterField.SetValue(state, 333);
            GlobalEpochField.SetValue(null, 0L);

            var success = Uuid7Optimized.TryNewUuid7(out var guid);

            Assert.True(success);
            Assert.NotEqual(Guid.Empty, guid);
            Assert.Equal(1, (int)CounterField.GetValue(state)!);
            var lastMs = (long)LastMsField.GetValue(state)!;
            Assert.True(lastMs > 0);
            Assert.True(Uuid7Optimized.GetGlobalEpoch() >= lastMs);
        }
        finally
        {
            Restore(state, snapshot);
        }
    }

    // On counter exhaustion the generator advances its logical millisecond
    // instead of waiting for the wall clock, so a backward clock step cannot stall generation.
    // Drift contract (decided 2026-11): on counter exhaustion the logical millisecond may lead the wall clock
    // by at most MaxLogicalLeadMs. At the limit the generator waits about one millisecond for the wall clock.
    // A lead beyond the limit means the clock moved backward, so the call fails rather than stall.
    [Fact]
    public void NewUuid7_WhenBurstExhaustsAtTheLeadLimit_WaitsAtMostAboutOneMillisecond()
    {
        var state = GetCurrentThreadState();
        var snapshot = Capture(state);
        try
        {
            var pinnedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Uuid7Optimized.MaxLogicalLeadMs;
            LastMsField.SetValue(state, pinnedMs);
            CounterField.SetValue(state, 4096);
            GlobalEpochField.SetValue(null, 0L);

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var guid = Uuid7Optimized.NewUuid7();
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);

            Assert.NotEqual(Guid.Empty, guid);
            Assert.True(elapsed < TimeSpan.FromMilliseconds(200), $"Waited {elapsed.TotalMilliseconds:F0} ms.");
            Assert.True((long)LastMsField.GetValue(state)! <= pinnedMs + 1);
        }
        finally
        {
            Restore(state, snapshot);
        }
    }

    [Fact]
    public void NewUuid7_WhenClockMovedBackwardBeyondTheLeadLimit_ThrowsInsteadOfStalling()
    {
        var state = GetCurrentThreadState();
        var snapshot = Capture(state);
        try
        {
            var pinnedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5_000;
            LastMsField.SetValue(state, pinnedMs);
            CounterField.SetValue(state, 4096);
            GlobalEpochField.SetValue(null, 0L);

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            Assert.Throws<InvalidOperationException>(() => Uuid7Optimized.NewUuid7());
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);

            Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Stalled {elapsed.TotalSeconds:F2}s before failing.");
        }
        finally
        {
            Restore(state, snapshot);
        }
    }

    [Fact]
    public void NewUuid7RfcBytes_WhenClockMovedBackwardBeyondTheLeadLimit_ThrowsInsteadOfStalling()
    {
        var state = GetCurrentThreadState();
        var snapshot = Capture(state);
        try
        {
            var pinnedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5_000;
            LastMsField.SetValue(state, pinnedMs);
            CounterField.SetValue(state, 4096);
            GlobalEpochField.SetValue(null, 0L);

            Assert.Throws<InvalidOperationException>(() => Uuid7Optimized.NewUuid7RfcBytes(new byte[16]));
        }
        finally
        {
            Restore(state, snapshot);
        }
    }

    [Fact]
    public void TryNewUuid7_WhenClockMovedBackwardBeyondTheLeadLimit_ReturnsFalse()
    {
        var state = GetCurrentThreadState();
        var snapshot = Capture(state);
        try
        {
            LastMsField.SetValue(state, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5_000);
            CounterField.SetValue(state, 4096);
            GlobalEpochField.SetValue(null, 0L);

            Assert.False(Uuid7Optimized.TryNewUuid7(out _));
        }
        finally
        {
            Restore(state, snapshot);
        }
    }

    // Sustained load: the embedded timestamp must stay within the lead limit (plus a small measurement
    // tolerance) of the wall clock, however fast the thread generates IDs.
    [Fact]
    public void SustainedLoad_KeepsTheTimestampWithinTheLeadLimitOfTheWallClock()
    {
        var state = GetCurrentThreadState();
        var snapshot = Capture(state);
        try
        {
            LastMsField.SetValue(state, 0L);
            CounterField.SetValue(state, 0);
            GlobalEpochField.SetValue(null, 0L);

            var worstLeadMs = 0L;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            Span<byte> bytes = stackalloc byte[16];
            var generated = 0;
            while (System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(1))
            {
                Uuid7Optimized.NewUuid7RfcBytes(bytes);
                generated++;
                if (generated % 4096 == 0)
                {
                    var stamp = ((long)bytes[0] << 40) | ((long)bytes[1] << 32) | ((long)bytes[2] << 24) |
                                ((long)bytes[3] << 16) | ((long)bytes[4] << 8) | bytes[5];
                    var lead = stamp - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    worstLeadMs = Math.Max(worstLeadMs, lead);
                }
            }

            Assert.True(worstLeadMs <= Uuid7Optimized.MaxLogicalLeadMs + 2,
                $"The timestamp led the wall clock by {worstLeadMs} ms after {generated} IDs.");
        }
        finally
        {
            Restore(state, snapshot);
        }
    }

    [Fact]
    public void NewUuid7RfcBytes_WhenRandomStateExhausts_SetsCounterPastMax()
    {
        var state = GetCurrentThreadState();
        var snapshot = Capture(state);
        try
        {
            var pinnedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 20;
            LastMsField.SetValue(state, pinnedMs);
            CounterField.SetValue(state, 0);
            RandAField.SetValue(state, RandAMask);
            RandBField.SetValue(state, RandBMask);
            GlobalEpochField.SetValue(null, 0L);

            Span<byte> bytes = stackalloc byte[16];
            Uuid7Optimized.NewUuid7RfcBytes(bytes);

            Assert.Equal(4096, (int)CounterField.GetValue(state)!);
            Assert.Equal(0x2, (bytes[8] >> 6) & 0x03);
        }
        finally
        {
            Restore(state, snapshot);
        }
    }

    [Fact]
    public void ReseedRandomState_WhenBufferNearEnd_RefillsAndResetsIndex()
    {
        var state = GetCurrentThreadState();
        var snapshot = Capture(state);
        try
        {
            var randomBuffer = (byte[])RandomBufferField.GetValue(state)!;
            RandomIndexField.SetValue(state, randomBuffer.Length - 5);

            ReseedRandomStateMethod.Invoke(null, new[] { state });

            Assert.Equal(10, (int)RandomIndexField.GetValue(state)!);
        }
        finally
        {
            Restore(state, snapshot);
        }
    }

    private static object GetCurrentThreadState()
    {
        var threadLocal = ThreadStateField.GetValue(null)!;
        var valueProperty = threadLocal.GetType().GetProperty("Value")!;
        return valueProperty.GetValue(threadLocal)!;
    }

    private static StateSnapshot Capture(object state)
    {
        return new StateSnapshot(
            (long)LastMsField.GetValue(state)!,
            (int)CounterField.GetValue(state)!,
            (ushort)RandAField.GetValue(state)!,
            (ulong)RandBField.GetValue(state)!,
            (int)RandomIndexField.GetValue(state)!,
            (long)GlobalEpochField.GetValue(null)!,
            (Uuid7Options)OptionsField.GetValue(null)!);
    }

    private static void Restore(object state, StateSnapshot snapshot)
    {
        LastMsField.SetValue(state, snapshot.LastMs);
        CounterField.SetValue(state, snapshot.Counter);
        RandAField.SetValue(state, snapshot.RandA);
        RandBField.SetValue(state, snapshot.RandB);
        RandomIndexField.SetValue(state, snapshot.RandomIndex);
        GlobalEpochField.SetValue(null, snapshot.GlobalEpoch);
        OptionsField.SetValue(null, snapshot.Options);
    }
}
