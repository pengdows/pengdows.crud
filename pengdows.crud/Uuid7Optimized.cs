// =============================================================================
// FILE: Uuid7Optimized.cs
// PURPOSE: High-performance, RFC 9562-compliant UUIDv7 generator with
//          configurable clock modes and thread-local state for lock-free operation.
//
// AI SUMMARY:
// - Generates time-sortable UUIDs following RFC 9562 (UUIDv7 specification).
// - Key advantages of UUIDv7 over UUIDv4:
//   * Chronologically sortable (great for database indexes)
//   * Contains timestamp (useful for debugging/auditing)
//   * No index fragmentation issues like random UUIDs
// - Performance optimizations:
//   * Thread-local counters eliminate CAS contention
//   * Buffered random bytes reduce syscall overhead
//   * Lock-free design for high throughput
// - Clock modes for different deployment scenarios:
//   * PtpSynced - Tight tolerance for PTP-synchronized clusters
//   * NtpSynced - Standard NTP environments (default)
//   * SingleInstance - Single-writer services
// - Throughput: Up to 4096 IDs per millisecond per thread.
// - Clock drift handling: Bounded backward drift with logical clock fallback.
// - TryNewUuid7() provides non-blocking generation for latency-sensitive code.
// =============================================================================

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace pengdows.crud;

/// <summary>
/// Clock synchronization mode for UUID7 generation.
/// Affects drift tolerance and wait behavior.
/// </summary>
public enum Uuid7ClockMode
{
    /// <summary>
    /// PTP/PHC disciplined clocks (±0.1–1.0 ms accuracy).
    /// Tight skew tolerance, shorter spin waits, prefer fail-fast on burst.
    /// Ideal for: PTP-synced clusters (EKS Nitro, on-prem PTP).
    /// </summary>
    PtpSynced,

    /// <summary>
    /// Standard NTP synchronization (±1–10 ms accuracy).
    /// Conservative skew tolerance, longer spin waits, blocking on burst.
    /// Ideal for: Most cloud environments with good NTP.
    /// </summary>
    NtpSynced,

    /// <summary>
    /// Single process/instance generating all writes.
    /// Generous skew tolerance, no cross-node ordering concerns.
    /// Ideal for: Single-writer services, embedded systems.
    /// </summary>
    SingleInstance
}

/// <summary>
/// Configuration options for UUID7 generation behavior.
/// </summary>
/// <param name="Mode">Clock synchronization mode (PTP, NTP, or single instance)</param>
/// <param name="MaxNegativeSkewMs">Maximum backward clock drift tolerated before using logical clock (ms)</param>
/// <param name="MaxSpinCount">Maximum spin-wait cycles before sleeping on counter overflow</param>
/// <param name="SleepMs">Sleep duration when spin limit exceeded (ms)</param>
/// <param name="FailFastOnBurst">If true, TryNewUuid7 returns false on counter overflow instead of blocking</param>
public sealed record Uuid7Options(
    Uuid7ClockMode Mode = Uuid7ClockMode.NtpSynced,
    int MaxNegativeSkewMs = 5,
    int MaxSpinCount = 128,
    int SleepMs = 1,
    bool FailFastOnBurst = false
);

/// <summary>
/// Production-ready UUIDv7 generator implementing RFC 9562 with optimizations for high throughput.
///
/// Features:
/// - Spec-compliant UUIDv7 generation
/// - Thread-local counters to eliminate CAS contention
/// - Buffered randomness to reduce syscall overhead
/// - Bounded clock drift handling
/// - Monotonic ordering within process scope (up to 4096 IDs/ms per thread)
/// - Configurable clock modes (PTP, NTP, SingleInstance) for different deployment scenarios
///
/// Monotonicity scope: Within a process, per logical clock. Not guaranteed across machines.
/// Throughput limit: 4096 IDs/ms per thread. Multiple threads can each generate 4096 IDs/ms independently.
/// If multiple threads exhaust counters simultaneously, each waits independently.
/// Clock rollback: Bounded drift with logical clock fallback.
/// </summary>
public static partial class Uuid7Optimized
{
    /// <summary>
    /// Thread-local state for lock-free UUID generation
    /// </summary>
    private sealed class V7ThreadState
    {
        public long LastMs;
        public int Counter; // 0..4095
        public ushort RandA; // 12-bit rand_a
        public ulong RandB; // 62-bit rand_b
        public readonly byte[] RandomBuffer = new byte[1024]; // Buffered random bytes - larger buffer for fewer refills
        public int RandomIndex;

        public V7ThreadState()
        {
            // Initialize with random data
            RandomNumberGenerator.Fill(RandomBuffer);
        }
    }

    private static readonly ThreadLocal<V7ThreadState> _threadState = new(() => new V7ThreadState());

    // Global epoch for cross-thread monotonicity hints
    private static long _globalEpochMs;

    // Configurable options (defaults to NTP mode)
    private static Uuid7Options _opts = DefaultsFor(Uuid7ClockMode.NtpSynced);

    // Counter limits (immutable)
    private const int CounterBits = 12;
    private const int CounterMax = (1 << CounterBits) - 1; // 4095
    private const ushort RandAMask = 0x0FFF;
    private const ulong RandBMask = (1UL << 62) - 1;

    /// <summary>
    /// Configure UUID7 generation behavior based on clock synchronization mode.
    /// Call this at application startup before generating any UUIDs.
    /// </summary>
    /// <param name="options">Configuration options, or null to use NTP defaults</param>
    public static void Configure(Uuid7Options? options = null)
    {
        if (options is null)
        {
            _opts = DefaultsFor(Uuid7ClockMode.NtpSynced);
            return;
        }

        // Get defaults for the mode
        var defaults = DefaultsFor(options.Mode);

        // Detect if user provided explicit non-default values
        // Record defaults: MaxNegativeSkewMs=5, MaxSpinCount=128, SleepMs=1, FailFastOnBurst=false
        var usedRecordDefaults =
            options.MaxNegativeSkewMs == 5 &&
            options.MaxSpinCount == 128 &&
            options.SleepMs == 1 &&
            options.FailFastOnBurst == false;

        // If user only specified Mode (using record defaults for everything else),
        // give them mode-specific defaults
        if (usedRecordDefaults)
        {
            _opts = defaults;
            return;
        }

        // User specified custom values - apply mode-specific clamping
        _opts = new Uuid7Options(
            options.Mode,
            options.Mode switch
            {
                Uuid7ClockMode.PtpSynced => Math.Min(options.MaxNegativeSkewMs, 1),
                Uuid7ClockMode.SingleInstance => options.MaxNegativeSkewMs,
                _ => Math.Max(options.MaxNegativeSkewMs, 5)
            },
            options.Mode switch
            {
                Uuid7ClockMode.PtpSynced => Math.Min(options.MaxSpinCount, 64),
                Uuid7ClockMode.SingleInstance => options.MaxSpinCount,
                _ => Math.Max(options.MaxSpinCount, 128)
            },
            options.SleepMs,
            options.FailFastOnBurst
        );
    }

    /// <summary>
    /// Get default options for a given clock mode
    /// </summary>
    private static Uuid7Options DefaultsFor(Uuid7ClockMode mode)
    {
        return mode switch
        {
            Uuid7ClockMode.PtpSynced => new Uuid7Options(
                mode,
                1,
                64,
                1,
                true),
            Uuid7ClockMode.SingleInstance => new Uuid7Options(
                mode,
                32,
                128,
                1,
                false),
            _ => new Uuid7Options(
                mode,
                5,
                128,
                1,
                false)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long UnixTimeMs()
    {
        var ticks = DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks;
        return ticks / TimeSpan.TicksPerMillisecond;
    }

    /// <summary>
    /// Generate a spec-compliant UUIDv7 with monotonic ordering and high performance.
    /// </summary>
    /// <returns>A new UUIDv7 following RFC 9562</returns>
    public static Guid NewUuid7()
    {
        var tls = _threadState.Value!;
        Span<byte> rfc = stackalloc byte[16];

        // Get current time and handle clock drift
        var nowMs = UnixTimeMs();
        var globalEpoch = Volatile.Read(ref _globalEpochMs);
        var usedMs = HandleClockDrift(nowMs, tls.LastMs, globalEpoch);

        // Handle millisecond boundary and counter management
        if (usedMs != tls.LastMs)
        {
            // New millisecond: reset counter and update global epoch
            tls.LastMs = usedMs;
            tls.Counter = 0;
            ReseedRandomState(tls);
            InterlockedMax(ref _globalEpochMs, usedMs);
        }

        // Check counter overflow
        if (tls.Counter > CounterMax)
        {
            // Counter exhausted: wait for next millisecond
            usedMs = BoundedWaitNextMs(usedMs);
            tls.LastMs = usedMs;
            tls.Counter = 0;
            ReseedRandomState(tls);
            InterlockedMax(ref _globalEpochMs, usedMs);
        }

        var randA = tls.RandA;
        var randB = tls.RandB;
        tls.Counter++;
        if (!IncrementRandState(tls))
        {
            tls.Counter = CounterMax + 1;
        }

        return BuildSpecCompliantGuid(usedMs, randA, randB, rfc);
    }

    /// <summary>
    /// Try to generate a UUIDv7 without waiting. Returns false if counter is exhausted for current millisecond.
    /// Useful for latency-sensitive scenarios where backpressure is preferred over blocking.
    /// </summary>
    /// <param name="result">The generated UUID, or default if generation failed</param>
    /// <returns>True if UUID was generated, false if caller should backoff/retry</returns>
    public static bool TryNewUuid7(out Guid result)
    {
        var tls = _threadState.Value!;
        Span<byte> rfc = stackalloc byte[16];

        var nowMs = UnixTimeMs();
        var globalEpoch = Volatile.Read(ref _globalEpochMs);
        var usedMs = HandleClockDrift(nowMs, tls.LastMs, globalEpoch);

        if (usedMs != tls.LastMs)
        {
            tls.LastMs = usedMs;
            tls.Counter = 0;
            ReseedRandomState(tls);
            InterlockedMax(ref _globalEpochMs, usedMs);
        }

        if (tls.Counter > CounterMax)
        {
            result = default;
            return false; // Caller decides to backoff/yield
        }

        var randA = tls.RandA;
        var randB = tls.RandB;
        tls.Counter++;
        if (!IncrementRandState(tls))
        {
            tls.Counter = CounterMax + 1;
        }
        result = BuildSpecCompliantGuid(usedMs, randA, randB, rfc);
        return true;
    }

    /// <summary>
    /// Generate a UUIDv7 directly to a byte span (avoids Guid allocation in .NET 8+).
    /// Writes in .NET Guid byte order (mixed-endian), not RFC/network order.
    /// </summary>
    /// <param name="dest">16-byte destination span</param>
    public static void NewUuid7Bytes(Span<byte> dest)
    {
        if (dest.Length < 16)
        {
            throw new ArgumentException("Destination must be at least 16 bytes", nameof(dest));
        }

        var guid = NewUuid7();
        guid.TryWriteBytes(dest);
    }

    /// <summary>
    /// Generate a UUIDv7 directly to a byte span in RFC/network order (big-endian).
    /// Useful for wire protocols and network transmission.
    /// </summary>
    /// <param name="dest">16-byte destination span</param>
    public static void NewUuid7RfcBytes(Span<byte> dest)
    {
        if (dest.Length < 16)
        {
            throw new ArgumentException("Destination must be at least 16 bytes", nameof(dest));
        }

        var tls = _threadState.Value!;
        Span<byte> rfc = stackalloc byte[16];

        var nowMs = UnixTimeMs();
        var globalEpoch = Volatile.Read(ref _globalEpochMs);
        var usedMs = HandleClockDrift(nowMs, tls.LastMs, globalEpoch);

        if (usedMs != tls.LastMs)
        {
            tls.LastMs = usedMs;
            tls.Counter = 0;
            ReseedRandomState(tls);
            InterlockedMax(ref _globalEpochMs, usedMs);
        }

        if (tls.Counter > CounterMax)
        {
            usedMs = BoundedWaitNextMs(usedMs);
            tls.LastMs = usedMs;
            tls.Counter = 0;
            ReseedRandomState(tls);
            InterlockedMax(ref _globalEpochMs, usedMs);
        }

        var randA = tls.RandA;
        var randB = tls.RandB;
        tls.Counter++;
        if (!IncrementRandState(tls))
        {
            tls.Counter = CounterMax + 1;
        }
        BuildRfcBytes(usedMs, randA, randB, rfc);
        rfc.CopyTo(dest);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long HandleClockDrift(long nowMs, long lastMs, long globalEpoch)
    {
        if (nowMs >= lastMs)
        {
            // Normal case: time is advancing
            return Math.Max(nowMs, globalEpoch);
        }

        // Clock went backward
        var drift = lastMs - nowMs;

        if (drift > _opts.MaxNegativeSkewMs)
        {
            // Large backward jump: use logical clock
            return Math.Max(lastMs, globalEpoch);
        }

        // Small drift: pin to last known time
        return lastMs;
    }

    private static long BoundedWaitNextMs(long currentMs)
    {
        var spinCount = 0;
        long newMs;

        do
        {
            if (spinCount < _opts.MaxSpinCount)
            {
                if (spinCount < 64)
                {
                    Thread.SpinWait(64); // Busy wait for first phase
                }
                else if (spinCount % 10 == 0)
                {
                    Thread.Yield(); // Be scheduler-friendly
                }
                else
                {
                    Thread.SpinWait(10);
                }

                spinCount++;
            }
            else
            {
                Thread.Sleep(_opts.SleepMs);
                spinCount = 0; // Reset spin count after sleep
            }

            newMs = UnixTimeMs();
        } while (newMs <= currentMs);

        return newMs;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InterlockedMax(ref long location, long value)
    {
        long current;
        do
        {
            current = Volatile.Read(ref location);
            if (value <= current)
            {
                return; // No update needed
            }
        } while (Interlocked.CompareExchange(ref location, value, current) != current);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillRandomSpan(Span<byte> dest, V7ThreadState tls)
    {
        if (tls.RandomIndex + dest.Length > tls.RandomBuffer.Length)
        {
            // Refill buffer when exhausted
            RandomNumberGenerator.Fill(tls.RandomBuffer);
            tls.RandomIndex = 0;
        }

        // Copy from buffer instead of syscall per UUID
        new Span<byte>(tls.RandomBuffer, tls.RandomIndex, dest.Length).CopyTo(dest);
        tls.RandomIndex += dest.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReseedRandomState(V7ThreadState tls)
    {
        Span<byte> random = stackalloc byte[10];
        FillRandomSpan(random, tls);

        tls.RandA = (ushort)(((random[0] << 8) | random[1]) & RandAMask);
        tls.RandB = BinaryPrimitives.ReadUInt64BigEndian(random.Slice(2, 8)) & RandBMask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IncrementRandState(V7ThreadState tls)
    {
        if (tls.RandB < RandBMask)
        {
            tls.RandB++;
            return true;
        }

        if (tls.RandA < RandAMask)
        {
            tls.RandB = 0;
            tls.RandA++;
            return true;
        }

        return false;
    }

    private static Guid BuildSpecCompliantGuid(long timestampMs, ushort randA, ulong randB, Span<byte> rfc)
    {
        BuildRfcBytes(timestampMs, randA, randB, rfc);

        // Convert RFC layout to .NET Guid byte order (mixed-endian)
        Span<byte> guidBytes = stackalloc byte[16];

        // time_low: bytes 0-3 as little-endian
        guidBytes[0] = rfc[3];
        guidBytes[1] = rfc[2];
        guidBytes[2] = rfc[1];
        guidBytes[3] = rfc[0];

        // time_mid: bytes 4-5 as little-endian
        guidBytes[4] = rfc[5];
        guidBytes[5] = rfc[4];

        // time_hi_and_version: bytes 6-7 as little-endian
        guidBytes[6] = rfc[7];
        guidBytes[7] = rfc[6];

        // clock_seq and node: bytes 8-15 as-is (big-endian)
        guidBytes[8] = rfc[8];
        guidBytes[9] = rfc[9];
        guidBytes[10] = rfc[10];
        guidBytes[11] = rfc[11];
        guidBytes[12] = rfc[12];
        guidBytes[13] = rfc[13];
        guidBytes[14] = rfc[14];
        guidBytes[15] = rfc[15];

        return new Guid(guidBytes);
    }

    private static void BuildRfcBytes(long timestampMs, ushort randA, ulong randB, Span<byte> rfc)
    {
        // 1. Build RFC 4122 layout (big-endian timestamp)

        // Timestamp: 48-bit big-endian (rfc[0..5])
        var ts = (ulong)timestampMs;
        rfc[0] = (byte)((ts >> 40) & 0xFF);
        rfc[1] = (byte)((ts >> 32) & 0xFF);
        rfc[2] = (byte)((ts >> 24) & 0xFF);
        rfc[3] = (byte)((ts >> 16) & 0xFF);
        rfc[4] = (byte)((ts >> 8) & 0xFF);
        rfc[5] = (byte)(ts & 0xFF);

        // time_hi_and_version: version=7 (0b0111) + 12-bit rand_a (monotonic field)
        randA &= RandAMask;
        rfc[6] = (byte)(0x70 | ((randA >> 8) & 0x0F)); // Version 7 + upper 4 bits of sequence
        rfc[7] = (byte)(randA & 0xFF); // Lower 8 bits of sequence

        // rand_b: 62-bit field with RFC 4122 variant (10xxxxxx in byte 8)
        randB &= RandBMask;
        rfc[8] = (byte)(0x80 | ((randB >> 56) & 0x3F)); // variant + top 6 bits
        rfc[9] = (byte)((randB >> 48) & 0xFF);
        rfc[10] = (byte)((randB >> 40) & 0xFF);
        rfc[11] = (byte)((randB >> 32) & 0xFF);
        rfc[12] = (byte)((randB >> 24) & 0xFF);
        rfc[13] = (byte)((randB >> 16) & 0xFF);
        rfc[14] = (byte)((randB >> 8) & 0xFF);
        rfc[15] = (byte)(randB & 0xFF);
    }

    /// <summary>
    /// Get current thread's counter state for diagnostics.
    /// Note: Values are read non-atomically and may be inconsistent during concurrent access.
    /// </summary>
    public static (long LastMs, int Counter, int RandomBufferIndex) GetThreadState()
    {
        var tls = _threadState.Value!;
        return (tls.LastMs, tls.Counter, tls.RandomIndex);
    }

    /// <summary>
    /// Get global epoch for diagnostics
    /// </summary>
    public static long GetGlobalEpoch()
    {
        return Volatile.Read(ref _globalEpochMs);
    }
}
