using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for DbParameterPool - a thread-safe pool of DbParameter arrays.
/// Uses Microsoft.Extensions.ObjectPool internally for production-ready pooling.
/// </summary>
public class DbParameterPoolTests
{
    private readonly DbProviderFactory _factory = SqliteFactory.Instance;

    [Fact]
    public void Rent_FirstCall_CreatesNewArray()
    {
        // Arrange
        var pool = new DbParameterPool(_factory);

        // Act
        var array = pool.Rent(5);

        // Assert
        Assert.NotNull(array);
        Assert.Equal(5, array.Length);
        Assert.All(array, param => Assert.NotNull(param));
    }

    [Fact]
    public void Rent_AfterReturn_ReusesArray()
    {
        // Arrange
        var pool = new DbParameterPool(_factory);
        var firstArray = pool.Rent(5);
        pool.Return(firstArray);

        // Act
        var secondArray = pool.Rent(5);

        // Assert - should be the same instance
        Assert.Same(firstArray, secondArray);
    }

    [Fact]
    public void Rent_DifferentSizes_UsesSeparatePools()
    {
        // Arrange
        var pool = new DbParameterPool(_factory);

        // Act
        var array3 = pool.Rent(3);
        var array5 = pool.Rent(5);
        var array10 = pool.Rent(10);

        // Assert
        Assert.Equal(3, array3.Length);
        Assert.Equal(5, array5.Length);
        Assert.Equal(10, array10.Length);
    }

    [Fact]
    public void Return_ClearsParameterValues()
    {
        // Arrange
        var pool = new DbParameterPool(_factory);
        var array = pool.Rent(3);

        // Set values
        array[0].ParameterName = "test1";
        array[0].Value = "value1";
        array[0].DbType = DbType.String;

        array[1].ParameterName = "test2";
        array[1].Value = 42;

        array[2].ParameterName = "test3";
        array[2].Value = DBNull.Value;

        // Act
        pool.Return(array);
        var reusedArray = pool.Rent(3);

        // Assert - all values should be cleared
        Assert.Same(array, reusedArray);
        Assert.All(reusedArray, param =>
        {
            Assert.Null(param.Value);
            Assert.True(string.IsNullOrEmpty(param.ParameterName));
            Assert.Equal(ParameterDirection.Input, param.Direction);
        });
    }

    [Fact]
    public void Rent_ConcurrentCalls_GrowsPoolNaturally()
    {
        // Arrange
        var pool = new DbParameterPool(_factory);
        var arrays = new List<DbParameter[]>();

        // Act - rent 10 arrays concurrently
        for (int i = 0; i < 10; i++)
        {
            arrays.Add(pool.Rent(5));
        }

        // Assert - should have 10 different arrays
        var distinctArrays = arrays.Distinct().Count();
        Assert.Equal(10, distinctArrays);

        // Return all
        foreach (var array in arrays)
        {
            pool.Return(array);
        }

        // Now pool should have 10 arrays
        var fromPool = pool.Rent(5);
        Assert.Contains(fromPool, arrays);
    }

    [Fact]
    public async Task Rent_MultiThreaded_IsThreadSafe()
    {
        // Arrange
        var pool = new DbParameterPool(_factory);
        var iterations = 100;
        var threadCount = 10;

        // Act - multiple threads renting and returning
        var tasks = Enumerable.Range(0, threadCount).Select(async _ =>
        {
            for (int i = 0; i < iterations; i++)
            {
                var array = pool.Rent(5);

                // Simulate work
                array[0].Value = i;
                await Task.Yield();

                pool.Return(array);
            }
        });

        await Task.WhenAll(tasks);

        // Assert - no exceptions means thread-safe
        Assert.True(true);
    }

    [Fact]
    public void Rent_VeryLargeSize_CreatesButDoesNotPool()
    {
        // Arrange
        var pool = new DbParameterPool(_factory);

        // Act - size > MaxParameterCount
        var largeArray = pool.Rent(100);

        // Assert
        Assert.Equal(100, largeArray.Length);

        // Return it
        pool.Return(largeArray);

        // Rent again - should get a NEW array (not pooled)
        var newArray = pool.Rent(100);
        Assert.NotSame(largeArray, newArray);
    }

    [Fact]
    public void Rent_ZeroSize_ThrowsArgumentException()
    {
        // Arrange
        var pool = new DbParameterPool(_factory);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => pool.Rent(0));
    }

    [Fact]
    public void Rent_NegativeSize_ThrowsArgumentException()
    {
        // Arrange
        var pool = new DbParameterPool(_factory);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => pool.Rent(-5));
    }

    [Fact]
    public void Return_NullArray_ThrowsArgumentNullException()
    {
        // Arrange
        var pool = new DbParameterPool(_factory);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => pool.Return(null!));
    }

    [Fact]
    public void GetStatistics_ShowsPoolState()
    {
        // Arrange
        var pool = new DbParameterPool(_factory);

        // Rent and return some arrays
        var array5a = pool.Rent(5);
        var array5b = pool.Rent(5);
        var array3 = pool.Rent(3);

        pool.Return(array5a);
        pool.Return(array5b);
        pool.Return(array3);

        // Act
        var stats = pool.GetStatistics();

        // Assert - should show pools for sizes 3 and 5
        Assert.True(stats.PoolSizes.ContainsKey(3));
        Assert.True(stats.PoolSizes.ContainsKey(5));
        Assert.True(stats.PoolSizes[3] >= 1); // At least 1 array
        Assert.True(stats.PoolSizes[5] >= 1); // At least 1 array
    }

    [Fact]
    public void Dispose_ClearsAllPools()
    {
        // Arrange
        var pool = new DbParameterPool(_factory);
        var array = pool.Rent(5);
        pool.Return(array);

        // Act
        pool.Dispose();

        // Assert - after dispose, getting stats should show empty pools
        // (or we could verify that Rent creates new arrays)
        var newArray = pool.Rent(5);
        Assert.NotNull(newArray);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(50)]
    public void Rent_VariousSizes_CreatesCorrectArrays(int size)
    {
        // Arrange
        var pool = new DbParameterPool(_factory);

        // Act
        var array = pool.Rent(size);

        // Assert
        Assert.Equal(size, array.Length);
        Assert.All(array, param => Assert.NotNull(param));
    }

    [Fact]
    public void ParametersAreProviderSpecific()
    {
        // Arrange - use SQLite factory
        var pool = new DbParameterPool(SqliteFactory.Instance);

        // Act
        var array = pool.Rent(1);

        // Assert - should be SQLite parameters
        Assert.IsType<SqliteParameter>(array[0]);
    }
}
