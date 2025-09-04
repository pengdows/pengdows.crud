using System.Collections.Concurrent;
using System.Text;

namespace pengdows.crud.infrastructure;

internal static class StringBuilderPool
{
    // Simple, bounded pool for StringBuilder to reduce short-lived allocations in hot paths.
    // Keep this conservative to avoid retaining large buffers across operations.

    private const int DefaultInitialCapacity = 256;
    private const int MaxRetainedCapacity = 4 * 1024; // 4KB
    private const int MaxPoolSize = 64;               // upper bound on pooled instances

    private static readonly ConcurrentBag<StringBuilder> _pool = new();
    private static int _count;

    public static StringBuilder Get()
    {
        if (_pool.TryTake(out var sb))
        {
            Interlocked.Decrement(ref _count);
            // Should already be cleared, but ensure
            sb.Clear();
            return sb;
        }

        return new StringBuilder(DefaultInitialCapacity);
    }

    public static StringBuilder Get(string? seed)
    {
        var sb = Get();
        if (!string.IsNullOrEmpty(seed))
        {
            sb.Append(seed);
        }
        return sb;
    }

    public static void Return(StringBuilder? sb)
    {
        if (sb is null)
        {
            return;
        }

        // Drop very large builders instead of retaining them
        if (sb.Capacity > MaxRetainedCapacity)
        {
            return;
        }

        // Bound pool size
        if (Interlocked.Increment(ref _count) > MaxPoolSize)
        {
            Interlocked.Decrement(ref _count);
            return;
        }

        sb.Clear();
        _pool.Add(sb);
    }
}

