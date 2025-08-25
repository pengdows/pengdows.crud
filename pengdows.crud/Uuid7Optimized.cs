namespace pengdows.crud;

using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;

/// <summary>
/// Production-ready UUIDv7 generator implementing RFC 9562 with optimizations for high throughput.
/// 
/// Features:
/// - Spec-compliant UUIDv7 generation
/// - Thread-local counters to eliminate CAS contention
/// - Buffered randomness to reduce syscall overhead
/// - Bounded clock drift handling
/// - Monotonic ordering within process scope (up to 4096 IDs/ms per thread)
/// 
/// Monotonicity scope: Within a process, per logical clock. Not guaranteed across machines.
/// Throughput limit: 4096 IDs/ms per thread. Multiple threads can each generate 4096 IDs/ms independently.
/// If multiple threads exhaust counters simultaneously, each waits independently.
/// Clock rollback: Bounded drift with logical clock fallback.
/// </summary>
public static class Uuid7Optimized
{
    /// <summary>
    /// Thread-local state for lock-free UUID generation
    /// </summary>
    private sealed class V7ThreadState
    {
        public long LastMs;
        public int Counter; // 0..4095
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
    
    // Clock drift policy
    private const long MaxNegativeSkewMs = 32; // Allow 32ms backward drift
    
    // Counter limits
    private const int CounterBits = 12;
    private const int CounterMax = (1 << CounterBits) - 1; // 4095
    
    // Bounded wait parameters
    private const int MaxSpinCount = 128;
    private const int SleepMs = 1;

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
            InterlockedMax(ref _globalEpochMs, usedMs);
        }
        
        // Check counter overflow
        if (tls.Counter > CounterMax)
        {
            // Counter exhausted: wait for next millisecond
            usedMs = BoundedWaitNextMs(usedMs);
            tls.LastMs = usedMs;
            tls.Counter = 0;
            InterlockedMax(ref _globalEpochMs, usedMs);
        }
        
        var sequence = tls.Counter++;
        
        return BuildSpecCompliantGuid(usedMs, sequence, tls, rfc);
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
            InterlockedMax(ref _globalEpochMs, usedMs);
        }

        if (tls.Counter > CounterMax)
        {
            result = default;
            return false; // Caller decides to backoff/yield
        }

        var sequence = tls.Counter++;
        result = BuildSpecCompliantGuid(usedMs, sequence, tls, rfc);
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
            InterlockedMax(ref _globalEpochMs, usedMs);
        }
        
        if (tls.Counter > CounterMax)
        {
            usedMs = BoundedWaitNextMs(usedMs);
            tls.LastMs = usedMs;
            tls.Counter = 0;
            InterlockedMax(ref _globalEpochMs, usedMs);
        }
        
        var sequence = tls.Counter++;
        BuildRfcBytes(usedMs, sequence, tls, rfc);
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
        
        if (drift > MaxNegativeSkewMs)
        {
            // Large backward jump: use logical clock
            return Math.Max(lastMs, globalEpoch);
        }
        else
        {
            // Small drift: pin to last known time
            return lastMs;
        }
    }

    private static long BoundedWaitNextMs(long currentMs)
    {
        var spinCount = 0;
        long newMs;
        
        do
        {
            if (spinCount < MaxSpinCount)
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
                Thread.Sleep(SleepMs);
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

    private static Guid BuildSpecCompliantGuid(long timestampMs, int sequence, V7ThreadState tls, Span<byte> rfc)
    {
        BuildRfcBytes(timestampMs, sequence, tls, rfc);
        
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

    private static void BuildRfcBytes(long timestampMs, int sequence, V7ThreadState tls, Span<byte> rfc)
    {
        // 1. Build RFC 4122 layout (big-endian timestamp)
        
        // Timestamp: 48-bit big-endian (rfc[0..5])
        rfc[0] = (byte)((timestampMs >> 40) & 0xFF);
        rfc[1] = (byte)((timestampMs >> 32) & 0xFF);
        rfc[2] = (byte)((timestampMs >> 24) & 0xFF);
        rfc[3] = (byte)((timestampMs >> 16) & 0xFF);
        rfc[4] = (byte)((timestampMs >> 8) & 0xFF);
        rfc[5] = (byte)(timestampMs & 0xFF);

        // time_hi_and_version: version=7 (0b0111) + 12-bit rand_a (monotonic sequence)
        var randA = (ushort)(sequence & 0x0FFF);
        rfc[6] = (byte)(0x70 | ((randA >> 8) & 0x0F)); // Version 7 + upper 4 bits of sequence
        rfc[7] = (byte)(randA & 0xFF); // Lower 8 bits of sequence

        // rand_b: 62 random bits with RFC 4122 variant (10xxxxxx in byte 8)
        Span<byte> randomBytes = stackalloc byte[8];
        FillRandomSpan(randomBytes, tls);
        
        // Set variant bits (10xxxxxx) while preserving 6 random bits
        rfc[8] = (byte)((randomBytes[0] & 0x3F) | 0x80); // variant + 6 random bits
        rfc[9] = randomBytes[1];
        rfc[10] = randomBytes[2];
        rfc[11] = randomBytes[3];
        rfc[12] = randomBytes[4];
        rfc[13] = randomBytes[5];
        rfc[14] = randomBytes[6];
        rfc[15] = randomBytes[7];
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
    public static long GetGlobalEpoch() => Volatile.Read(ref _globalEpochMs);
}
