using System;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class Uuid7OptimizedCoveragePushTests
{
    private static readonly BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo ThreadStateField =
        typeof(Uuid7Optimized).GetField("_threadState", NonPublicStatic)!;
    private static readonly FieldInfo GlobalEpochField =
        typeof(Uuid7Optimized).GetField("_globalEpochMs", NonPublicStatic)!;
    private static readonly FieldInfo OptionsField =
        typeof(Uuid7Optimized).GetField("_opts", NonPublicStatic)!;
    private static readonly MethodInfo BoundedWaitNextMsMethod =
        typeof(Uuid7Optimized).GetMethod("BoundedWaitNextMs", NonPublicStatic)!;
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

    [Fact]
    public void NewUuid7_WhenCounterOverflows_UsesBoundedWaitPath()
    {
        var state = GetCurrentThreadState();
        var snapshot = Capture(state);
        try
        {
            var pinnedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 120;
            LastMsField.SetValue(state, pinnedMs);
            CounterField.SetValue(state, 4096);
            GlobalEpochField.SetValue(null, 0L);

            var guid = Uuid7Optimized.NewUuid7();

            Assert.NotEqual(Guid.Empty, guid);
            Assert.Equal(1, (int)CounterField.GetValue(state)!);
            Assert.True((long)LastMsField.GetValue(state)! > pinnedMs);
        }
        finally
        {
            Restore(state, snapshot);
        }
    }

    [Fact]
    public void NewUuid7RfcBytes_WhenCounterOverflows_UsesBoundedWaitPath()
    {
        var state = GetCurrentThreadState();
        var snapshot = Capture(state);
        try
        {
            var pinnedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 120;
            LastMsField.SetValue(state, pinnedMs);
            CounterField.SetValue(state, 4096);
            GlobalEpochField.SetValue(null, 0L);

            Span<byte> bytes = stackalloc byte[16];
            Uuid7Optimized.NewUuid7RfcBytes(bytes);

            Assert.Equal(1, (int)CounterField.GetValue(state)!);
            Assert.True((long)LastMsField.GetValue(state)! > pinnedMs);
            Assert.Equal(0x7, (bytes[6] >> 4) & 0x0F);
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
    public void BoundedWaitNextMs_CoversSpinYieldAndSleepBranches()
    {
        var originalOptions = (Uuid7Options)OptionsField.GetValue(null)!;
        try
        {
            // Force enough wait time to exercise spin/yield branches (including spinCount >= 64).
            OptionsField.SetValue(null, new Uuid7Options(
                Uuid7ClockMode.SingleInstance,
                MaxNegativeSkewMs: 32,
                MaxSpinCount: 80,
                SleepMs: 0,
                FailFastOnBurst: false));

            var currentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 50;
            var waited = (long)BoundedWaitNextMsMethod.Invoke(null, new object[] { currentMs })!;
            Assert.True(waited > currentMs);

            // Force sleep branch (MaxSpinCount == 0 goes straight to sleep path).
            OptionsField.SetValue(null, new Uuid7Options(
                Uuid7ClockMode.SingleInstance,
                MaxNegativeSkewMs: 32,
                MaxSpinCount: 0,
                SleepMs: 1,
                FailFastOnBurst: false));

            var currentMs2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 2;
            var waited2 = (long)BoundedWaitNextMsMethod.Invoke(null, new object[] { currentMs2 })!;
            Assert.True(waited2 > currentMs2);
        }
        finally
        {
            OptionsField.SetValue(null, originalOptions);
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
