using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.ObjectPool;

namespace pengdows.crud.@internal;

/// <summary>
/// Thread-safe pool of DbParameter arrays for performance optimization.
/// Uses Microsoft.Extensions.ObjectPool for production-ready pooling.
/// Grows dynamically based on actual workload demand.
/// </summary>
/// <remarks>
/// Design:
/// - Separate pools per array size (3 params, 5 params, etc.)
/// - Uses DefaultObjectPool with custom policy for lifecycle management
/// - Clears parameter values on return to prevent memory leaks
/// - Does not pool very large arrays (>MaxParameterCount)
/// </remarks>
internal class DbParameterPool : IDisposable
{
    private readonly DbProviderFactory _factory;
    private readonly ConcurrentDictionary<int, ObjectPool<DbParameter[]>> _pools = new();
    private const int MaxParameterCount = 50;

    public DbParameterPool(DbProviderFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Rent a parameter array from the pool.
    /// If pool is empty, creates a new array.
    /// </summary>
    /// <param name="count">Number of parameters needed</param>
    /// <returns>Array of DbParameters</returns>
    public DbParameter[] Rent(int count)
    {
        if (count <= 0)
            throw new ArgumentException("Count must be greater than zero", nameof(count));

        // Don't pool very large arrays
        if (count > MaxParameterCount)
        {
            return CreateParameterArray(count);
        }

        // Get or create pool for this size
        var pool = _pools.GetOrAdd(count, size =>
        {
            var policy = new DbParameterArrayPolicy(_factory, size);
            return new DefaultObjectPool<DbParameter[]>(policy);
        });

        return pool.Get();
    }

    /// <summary>
    /// Return a parameter array to the pool.
    /// Parameters are cleared before being pooled.
    /// </summary>
    /// <param name="array">Array to return</param>
    public void Return(DbParameter[] array)
    {
        if (array == null)
            throw new ArgumentNullException(nameof(array));

        // Don't pool very large arrays
        if (array.Length > MaxParameterCount)
            return;

        // Get pool for this size
        if (_pools.TryGetValue(array.Length, out var pool))
        {
            pool.Return(array);
        }
    }

    /// <summary>
    /// Get statistics about pool state for diagnostics
    /// </summary>
    public PoolStatistics GetStatistics()
    {
        var stats = new PoolStatistics();

        foreach (var kvp in _pools)
        {
            // Note: ObjectPool doesn't expose count directly
            // This is an approximation based on pool existence
            if (kvp.Value != null)
            {
                stats.PoolSizes[kvp.Key] = 1; // Pool exists
            }
        }

        return stats;
    }

    /// <summary>
    /// Create a parameter array without using the pool
    /// </summary>
    private DbParameter[] CreateParameterArray(int count)
    {
        var array = new DbParameter[count];
        for (int i = 0; i < count; i++)
        {
            array[i] = _factory.CreateParameter()!;
        }
        return array;
    }

    public void Dispose()
    {
        // ObjectPool doesn't need explicit disposal
        // Just clear our dictionary
        _pools.Clear();
    }

    /// <summary>
    /// Policy for ObjectPool - defines how to create and reset DbParameter arrays
    /// </summary>
    private class DbParameterArrayPolicy : IPooledObjectPolicy<DbParameter[]>
    {
        private readonly DbProviderFactory _factory;
        private readonly int _size;

        public DbParameterArrayPolicy(DbProviderFactory factory, int size)
        {
            _factory = factory;
            _size = size;
        }

        public DbParameter[] Create()
        {
            var array = new DbParameter[_size];
            for (int i = 0; i < _size; i++)
            {
                array[i] = _factory.CreateParameter()!;
            }
            return array;
        }

        public bool Return(DbParameter[] obj)
        {
            // Clear all parameter values before returning to pool
            // This prevents memory leaks from holding object references
            for (int i = 0; i < obj.Length; i++)
            {
                var param = obj[i];
                param.Value = null;
                param.ParameterName = string.Empty;
                param.Direction = ParameterDirection.Input;
                param.DbType = DbType.Object; // Reset to default
            }

            return true; // true = return to pool, false = discard
        }
    }
}

/// <summary>
/// Statistics about pool state
/// </summary>
public class PoolStatistics
{
    public Dictionary<int, int> PoolSizes { get; } = new();
}
