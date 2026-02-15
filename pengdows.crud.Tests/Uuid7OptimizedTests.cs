using System;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

public class Uuid7OptimizedTests
{
    // Reset configuration to default before each test to ensure isolation
    public Uuid7OptimizedTests()
    {
        // Reset to default NTP mode
        Uuid7Optimized.Configure(new Uuid7Options(Uuid7ClockMode.NtpSynced));
    }

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
        var dest = new byte[15];
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
    public void NewUuid7RfcBytes_IncrementsRandField_WhenTimestampPinned()
    {
        var tlsField = typeof(Uuid7Optimized).GetField("_threadState", BindingFlags.NonPublic | BindingFlags.Static)!;
        var threadLocal = tlsField.GetValue(null)!;
        var valueProp = threadLocal.GetType().GetProperty("Value")!;
        var state = valueProp.GetValue(threadLocal)!;
        var lastMsField = state.GetType().GetField("LastMs")!;
        var counterField = state.GetType().GetField("Counter")!;

        var originalLastMs = (long)lastMsField.GetValue(state)!;
        var originalCounter = (int)counterField.GetValue(state)!;

        try
        {
            var pinnedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10_000;
            lastMsField.SetValue(state, pinnedMs);
            counterField.SetValue(state, 0);

            Span<byte> first = stackalloc byte[16];
            Span<byte> second = stackalloc byte[16];

            Uuid7Optimized.NewUuid7RfcBytes(first);
            Uuid7Optimized.NewUuid7RfcBytes(second);

            var firstRand = ExtractRand74(first);
            var secondRand = ExtractRand74(second);

            Assert.Equal(firstRand + 1, secondRand);
        }
        finally
        {
            counterField.SetValue(state, originalCounter);
            lastMsField.SetValue(state, originalLastMs);
        }
    }

    [Fact]
    public void NewUuid7RfcBytes_ThrowsWhenSpanTooSmall()
    {
        var dest = new byte[15];
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
        var epochField =
            typeof(Uuid7Optimized).GetField("_globalEpochMs", BindingFlags.NonPublic | BindingFlags.Static)!;
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

    #region Configuration Tests (TDD - Tests Written First)

    [Fact]
    public void Configure_WithNull_UsesNtpDefaults()
    {
        // Act
        Uuid7Optimized.Configure(null);

        // Assert - verify via reflection that defaults are applied
        var optsField = typeof(Uuid7Optimized).GetField("_opts", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(optsField);
        var opts = optsField!.GetValue(null) as Uuid7Options;
        Assert.NotNull(opts);
        Assert.Equal(Uuid7ClockMode.NtpSynced, opts!.Mode);
        Assert.Equal(5, opts.MaxNegativeSkewMs);
        Assert.Equal(128, opts.MaxSpinCount);
        Assert.Equal(1, opts.SleepMs);
        Assert.False(opts.FailFastOnBurst);
    }

    [Fact]
    public void Configure_PtpMode_UsesTightDefaults()
    {
        // Act - Pass null to get mode-specific defaults would require calling internal DefaultsFor
        // Instead, call Configure with null then check internal _opts
        // For PTP mode specifically, we test by calling Configure(null) which gives NTP defaults
        // then we separately test the DefaultsFor logic through actual usage

        // To get PTP defaults, user should not construct Uuid7Options manually
        // They should use the pattern that Configure detects
        Uuid7Optimized.Configure(new Uuid7Options(Uuid7ClockMode.PtpSynced));

        // Assert - When user only sets Mode (all other values are record defaults),
        // Configure() detects this and applies mode-specific defaults
        var optsField = typeof(Uuid7Optimized).GetField("_opts", BindingFlags.NonPublic | BindingFlags.Static);
        var opts = optsField!.GetValue(null) as Uuid7Options;
        Assert.NotNull(opts);
        Assert.Equal(Uuid7ClockMode.PtpSynced, opts!.Mode);
        Assert.Equal(1, opts.MaxNegativeSkewMs); // Tight skew for PTP
        Assert.Equal(64, opts.MaxSpinCount); // Shorter spin for PTP
        Assert.Equal(1, opts.SleepMs);
        Assert.True(opts.FailFastOnBurst); // PTP mode prefers fail-fast
    }

    [Fact]
    public void Configure_NtpMode_UsesConservativeDefaults()
    {
        // Act
        Uuid7Optimized.Configure(new Uuid7Options(Uuid7ClockMode.NtpSynced));

        // Assert
        var optsField = typeof(Uuid7Optimized).GetField("_opts", BindingFlags.NonPublic | BindingFlags.Static);
        var opts = optsField!.GetValue(null) as Uuid7Options;
        Assert.NotNull(opts);
        Assert.Equal(Uuid7ClockMode.NtpSynced, opts!.Mode);
        Assert.Equal(5, opts.MaxNegativeSkewMs); // Conservative for NTP
        Assert.Equal(128, opts.MaxSpinCount); // Longer spin
        Assert.Equal(1, opts.SleepMs);
        Assert.False(opts.FailFastOnBurst); // Blocking is OK
    }

    [Fact]
    public void Configure_SingleInstanceMode_UsesGenerousDefaults()
    {
        // Act
        Uuid7Optimized.Configure(new Uuid7Options(Uuid7ClockMode.SingleInstance));

        // Assert
        var optsField = typeof(Uuid7Optimized).GetField("_opts", BindingFlags.NonPublic | BindingFlags.Static);
        var opts = optsField!.GetValue(null) as Uuid7Options;
        Assert.NotNull(opts);
        Assert.Equal(Uuid7ClockMode.SingleInstance, opts!.Mode);
        Assert.Equal(32, opts.MaxNegativeSkewMs); // Generous for single instance
        Assert.Equal(128, opts.MaxSpinCount);
        Assert.Equal(1, opts.SleepMs);
        Assert.False(opts.FailFastOnBurst);
    }

    [Fact]
    public void Configure_PtpMode_WithCustomMaxNegativeSkew_ClampsToMaximum()
    {
        // Arrange - Try to set higher than PTP should allow
        var options = new Uuid7Options(
            Uuid7ClockMode.PtpSynced,
            10 // Too high for PTP
        );

        // Act
        Uuid7Optimized.Configure(options);

        // Assert - Should be clamped to 1ms for PTP
        var optsField = typeof(Uuid7Optimized).GetField("_opts", BindingFlags.NonPublic | BindingFlags.Static);
        var opts = optsField!.GetValue(null) as Uuid7Options;
        Assert.NotNull(opts);
        Assert.Equal(1, opts!.MaxNegativeSkewMs);
    }

    [Fact]
    public void Configure_NtpMode_WithCustomMaxNegativeSkew_ClampsToMinimum()
    {
        // Arrange - Try to set lower than NTP should allow
        var options = new Uuid7Options(
            Uuid7ClockMode.NtpSynced,
            2 // Too low for NTP
        );

        // Act
        Uuid7Optimized.Configure(options);

        // Assert - Should be raised to 5ms for NTP
        var optsField = typeof(Uuid7Optimized).GetField("_opts", BindingFlags.NonPublic | BindingFlags.Static);
        var opts = optsField!.GetValue(null) as Uuid7Options;
        Assert.NotNull(opts);
        Assert.Equal(5, opts!.MaxNegativeSkewMs);
    }

    [Fact]
    public void Configure_PtpMode_WithCustomMaxSpinCount_ClampsToMaximum()
    {
        // Arrange
        var options = new Uuid7Options(
            Uuid7ClockMode.PtpSynced,
            MaxSpinCount: 256 // Too high for PTP
        );

        // Act
        Uuid7Optimized.Configure(options);

        // Assert - Should be clamped to 64 for PTP
        var optsField = typeof(Uuid7Optimized).GetField("_opts", BindingFlags.NonPublic | BindingFlags.Static);
        var opts = optsField!.GetValue(null) as Uuid7Options;
        Assert.NotNull(opts);
        Assert.Equal(64, opts!.MaxSpinCount);
    }

    [Fact]
    public void Configure_NtpMode_WithCustomMaxSpinCount_ClampsToMinimum()
    {
        // Arrange
        var options = new Uuid7Options(
            Uuid7ClockMode.NtpSynced,
            MaxSpinCount: 32 // Too low for NTP
        );

        // Act
        Uuid7Optimized.Configure(options);

        // Assert - Should be raised to 128 for NTP
        var optsField = typeof(Uuid7Optimized).GetField("_opts", BindingFlags.NonPublic | BindingFlags.Static);
        var opts = optsField!.GetValue(null) as Uuid7Options;
        Assert.NotNull(opts);
        Assert.Equal(128, opts!.MaxSpinCount);
    }

    [Fact]
    public void Configure_SingleInstanceMode_DoesNotClampCustomValues()
    {
        // Arrange
        var options = new Uuid7Options(
            Uuid7ClockMode.SingleInstance,
            100,
            256
        );

        // Act
        Uuid7Optimized.Configure(options);

        // Assert - SingleInstance mode preserves custom values
        var optsField = typeof(Uuid7Optimized).GetField("_opts", BindingFlags.NonPublic | BindingFlags.Static);
        var opts = optsField!.GetValue(null) as Uuid7Options;
        Assert.NotNull(opts);
        Assert.Equal(100, opts!.MaxNegativeSkewMs);
        Assert.Equal(256, opts.MaxSpinCount);
    }

    [Fact]
    public void Configure_CanOverrideFailFastOnBurst()
    {
        // Arrange - To override FailFastOnBurst from PTP default (true) to false,
        // we need to set at least one OTHER non-default value so Configure doesn't
        // think we want mode defaults. Set MaxNegativeSkewMs to something custom.
        var options = new Uuid7Options(
            Uuid7ClockMode.PtpSynced,
            1, // Set to PTP's expected value explicitly
            64, // Set to PTP's expected value explicitly
            1, // Set to expected value
            false // Override PTP default (which is true)
        );

        // Act
        Uuid7Optimized.Configure(options);

        // Assert - Should respect explicit FailFastOnBurst = false
        var optsField = typeof(Uuid7Optimized).GetField("_opts", BindingFlags.NonPublic | BindingFlags.Static);
        var opts = optsField!.GetValue(null) as Uuid7Options;
        Assert.NotNull(opts);
        Assert.False(opts!.FailFastOnBurst);
        Assert.Equal(1, opts.MaxNegativeSkewMs); // Should be clamped/kept at 1
        Assert.Equal(64, opts.MaxSpinCount); // Should be clamped/kept at 64
    }

    [Fact]
    public void Uuid7Options_DefaultConstructor_UsesNtpDefaults()
    {
        // Act
        var options = new Uuid7Options();

        // Assert
        Assert.Equal(Uuid7ClockMode.NtpSynced, options.Mode);
        Assert.Equal(5, options.MaxNegativeSkewMs);
        Assert.Equal(128, options.MaxSpinCount);
        Assert.Equal(1, options.SleepMs);
        Assert.False(options.FailFastOnBurst);
    }

    [Fact]
    public void Uuid7ClockMode_HasExpectedValues()
    {
        // Assert enum exists and has expected values
        Assert.True(Enum.IsDefined(typeof(Uuid7ClockMode), Uuid7ClockMode.PtpSynced));
        Assert.True(Enum.IsDefined(typeof(Uuid7ClockMode), Uuid7ClockMode.NtpSynced));
        Assert.True(Enum.IsDefined(typeof(Uuid7ClockMode), Uuid7ClockMode.SingleInstance));
    }

    [Theory]
    [InlineData(Uuid7ClockMode.PtpSynced)]
    [InlineData(Uuid7ClockMode.NtpSynced)]
    [InlineData(Uuid7ClockMode.SingleInstance)]
    public void Configure_AllModes_GeneratesValidUuid7(Uuid7ClockMode mode)
    {
        // Arrange
        Uuid7Optimized.Configure(new Uuid7Options(mode));

        // Act
        var guid = Uuid7Optimized.NewUuid7();
        var bytes = guid.ToByteArray();

        // Assert - Still generates valid UUIDv7
        var version = (bytes[7] >> 4) & 0x0F;
        var variant = (bytes[8] >> 6) & 0x03;
        Assert.Equal(0x7, version);
        Assert.Equal(0x2, variant);
        Assert.NotEqual(Guid.Empty, guid);
    }

    #endregion

    private static UInt128 ExtractRand74(ReadOnlySpan<byte> rfc)
    {
        var randA = (ushort)(((rfc[6] & 0x0F) << 8) | rfc[7]);
        var randB =
            ((ulong)(rfc[8] & 0x3F) << 56) |
            ((ulong)rfc[9] << 48) |
            ((ulong)rfc[10] << 40) |
            ((ulong)rfc[11] << 32) |
            ((ulong)rfc[12] << 24) |
            ((ulong)rfc[13] << 16) |
            ((ulong)rfc[14] << 8) |
            rfc[15];

        return ((UInt128)randA << 62) | randB;
    }
}
