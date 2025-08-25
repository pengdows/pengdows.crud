using System;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

public class Uuid7OptimizedTests
{
    [Fact]
    public void NewUuid7_GeneratesVersion7AndRfcVariant()
    {
        var guid = Uuid7Optimized.NewUuid7();
        var bytes = guid.ToByteArray();

        var version = (bytes[7] >> 4) & 0x0F;
        var variant = (bytes[8] >> 6) & 0x03;

        Assert.Equal(0x7, version);
        Assert.Equal(0x2, variant);
    }

    [Fact]
    public void NewUuid7Bytes_WritesBytes_WithVersionAndVariant()
    {
        Span<byte> dest = stackalloc byte[16];
        Uuid7Optimized.NewUuid7Bytes(dest);

        var version = (dest[7] >> 4) & 0x0F;
        var variant = (dest[8] >> 6) & 0x03;

        Assert.Equal(0x7, version);
        Assert.Equal(0x2, variant);
    }

    [Fact]
    public void NewUuid7Bytes_ThrowsWhenSpanTooSmall()
    {
        byte[] dest = new byte[15];
        Assert.Throws<ArgumentException>(() => Uuid7Optimized.NewUuid7Bytes(dest));
    }

    [Fact]
    public void NewUuid7RfcBytes_WritesRfcOrder_WithVersionAndVariant()
    {
        Span<byte> dest = stackalloc byte[16];
        Uuid7Optimized.NewUuid7RfcBytes(dest);

        var version = (dest[6] >> 4) & 0x0F;
        var variant = (dest[8] >> 6) & 0x03;

        Assert.Equal(0x7, version);
        Assert.Equal(0x2, variant);
    }

    [Fact]
    public void NewUuid7RfcBytes_ThrowsWhenSpanTooSmall()
    {
        byte[] dest = new byte[15];
        Assert.Throws<ArgumentException>(() => Uuid7Optimized.NewUuid7RfcBytes(dest));
    }

    [Fact]
    public void TryNewUuid7_ReturnsTrueAndGuid()
    {
        var success = Uuid7Optimized.TryNewUuid7(out var guid);
        Assert.True(success);
        Assert.NotEqual(Guid.Empty, guid);
    }

    [Fact]
    public void TryNewUuid7_ReturnsFalseWhenCounterExhausted()
    {
        Uuid7Optimized.NewUuid7();
        var field = typeof(Uuid7Optimized).GetField("_threadState", BindingFlags.NonPublic | BindingFlags.Static)!;
        var threadLocal = field.GetValue(null)!;
        var valueProp = threadLocal.GetType().GetProperty("Value")!;
        var state = valueProp.GetValue(threadLocal)!;
        var counterField = state.GetType().GetField("Counter")!;
        var lastMsField = state.GetType().GetField("LastMs")!;

        var originalCounter = (int)counterField.GetValue(state)!;
        var originalLastMs = (long)lastMsField.GetValue(state)!;

        try
        {
            lastMsField.SetValue(state, long.MaxValue);
            counterField.SetValue(state, 4096);

            var result = Uuid7Optimized.TryNewUuid7(out var guid);
            Assert.False(result);
            Assert.Equal(Guid.Empty, guid);
        }
        finally
        {
            counterField.SetValue(state, originalCounter);
            lastMsField.SetValue(state, originalLastMs);
        }
    }

    [Fact]
    public void GetThreadState_BeforeAndAfterGeneration()
    {
        var tlsField = typeof(Uuid7Optimized).GetField("_threadState", BindingFlags.NonPublic | BindingFlags.Static)!;
        var threadLocal = tlsField.GetValue(null)!;
        var valueProp = threadLocal.GetType().GetProperty("Value")!;
        var state = valueProp.GetValue(threadLocal)!;
        state.GetType().GetField("LastMs")!.SetValue(state, 0L);
        state.GetType().GetField("Counter")!.SetValue(state, 0);

        var before = Uuid7Optimized.GetThreadState();
        Assert.Equal(0, before.Counter);
        Assert.Equal(0, before.LastMs);

        Uuid7Optimized.NewUuid7();

        var after = Uuid7Optimized.GetThreadState();
        Assert.True(after.LastMs >= before.LastMs);
        Assert.True(after.Counter > before.Counter);
    }

    [Fact]
    public void GetGlobalEpoch_UpdatesWithUuidGeneration()
    {
        var epochField = typeof(Uuid7Optimized).GetField("_globalEpochMs", BindingFlags.NonPublic | BindingFlags.Static)!;
        epochField.SetValue(null, 0L);
        var tlsField = typeof(Uuid7Optimized).GetField("_threadState", BindingFlags.NonPublic | BindingFlags.Static)!;
        var threadLocal = tlsField.GetValue(null)!;
        var valueProp = threadLocal.GetType().GetProperty("Value")!;
        var state = valueProp.GetValue(threadLocal)!;
        state.GetType().GetField("LastMs")!.SetValue(state, 0L);
        state.GetType().GetField("Counter")!.SetValue(state, 0);

        var before = Uuid7Optimized.GetGlobalEpoch();
        Assert.Equal(0, before);

        Uuid7Optimized.NewUuid7();

        var after = Uuid7Optimized.GetGlobalEpoch();
        Assert.True(after > before);
    }

    [Fact]
    public void NewUuid7_CounterOverflow_WaitsForNextMillisecond()
    {
        Uuid7Optimized.NewUuid7();
        var field = typeof(Uuid7Optimized).GetField("_threadState", BindingFlags.NonPublic | BindingFlags.Static)!;
        var threadLocal = field.GetValue(null)!;
        var valueProp = threadLocal.GetType().GetProperty("Value")!;
        var state = valueProp.GetValue(threadLocal)!;
        var counterField = state.GetType().GetField("Counter")!;
        var lastMsField = state.GetType().GetField("LastMs")!;

        var lastMs = (long)lastMsField.GetValue(state)!;

        counterField.SetValue(state, 4096);
        lastMsField.SetValue(state, lastMs);

        var guid = Uuid7Optimized.NewUuid7();
        Assert.NotEqual(Guid.Empty, guid);

        var updated = Uuid7Optimized.GetThreadState();
        Assert.True(updated.LastMs > lastMs);
        Assert.Equal(1, updated.Counter);
    }
}
