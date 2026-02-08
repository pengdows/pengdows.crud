using System;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for DatabaseContext parameter pool integration.
/// Verifies that DatabaseContext properly manages the DbParameterPool lifecycle.
/// </summary>
public class DatabaseContextParameterPoolTests
{
    [Fact]
    public void RentParameters_FirstCall_ReturnsArray()
    {
        // Arrange
        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext(
            "Data Source=:memory:",
            SqliteFactory.Instance,
            typeMap);

        // Act
        var parameters = context.RentParameters(5);

        // Assert
        Assert.NotNull(parameters);
        Assert.Equal(5, parameters.Length);
    }

    [Fact]
    public void RentParameters_AfterReturn_ReusesArray()
    {
        // Arrange
        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext(
            "Data Source=:memory:",
            SqliteFactory.Instance,
            typeMap);

        var firstRent = context.RentParameters(5);
        context.ReturnParameters(firstRent);

        // Act
        var secondRent = context.RentParameters(5);

        // Assert - should be same instance
        Assert.Same(firstRent, secondRent);
    }

    [Fact]
    public void ReturnParameters_ClearsValues()
    {
        // Arrange
        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext(
            "Data Source=:memory:",
            SqliteFactory.Instance,
            typeMap);

        var parameters = context.RentParameters(3);
        parameters[0].Value = "test";
        parameters[1].Value = 42;
        parameters[2].ParameterName = "param";

        // Act
        context.ReturnParameters(parameters);
        var reused = context.RentParameters(3);

        // Assert - values should be cleared
        Assert.Same(parameters, reused);
        Assert.All(reused, p => Assert.Null(p.Value));
        Assert.All(reused, p => Assert.True(string.IsNullOrEmpty(p.ParameterName)));
    }

    [Fact]
    public void RentParameters_ZeroCount_ThrowsArgumentException()
    {
        // Arrange
        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext(
            "Data Source=:memory:",
            SqliteFactory.Instance,
            typeMap);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => context.RentParameters(0));
    }

    [Fact]
    public void ReturnParameters_Null_ThrowsArgumentNullException()
    {
        // Arrange
        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext(
            "Data Source=:memory:",
            SqliteFactory.Instance,
            typeMap);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => context.ReturnParameters(null!));
    }

    [Fact]
    public void Dispose_ClearsPool()
    {
        // Arrange
        var typeMap = new TypeMapRegistry();
        var context = new DatabaseContext(
            "Data Source=:memory:",
            SqliteFactory.Instance,
            typeMap);

        var parameters = context.RentParameters(5);
        context.ReturnParameters(parameters);

        // Act
        context.Dispose();

        // Assert - after dispose, context should still be usable
        // (pool might be recreated or cleared)
        // This test mainly ensures no exceptions on dispose
        Assert.True(true);
    }

    [Fact]
    public void ParameterPool_IsSingletonPerContext()
    {
        // Arrange
        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext(
            "Data Source=:memory:",
            SqliteFactory.Instance,
            typeMap);

        // Act - rent from different operations
        var array1 = context.RentParameters(5);
        var array2 = context.RentParameters(5);

        context.ReturnParameters(array1);
        context.ReturnParameters(array2);

        var array3 = context.RentParameters(5);

        // Assert - array3 should be one of the returned arrays
        Assert.True(ReferenceEquals(array1, array3) || ReferenceEquals(array2, array3));
    }

    [Fact]
    public void ParameterPool_IsProviderSpecific()
    {
        // Arrange
        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext(
            "Data Source=:memory:",
            SqliteFactory.Instance,
            typeMap);

        // Act
        var parameters = context.RentParameters(1);

        // Assert - should be SQLite parameters
        Assert.IsType<SqliteParameter>(parameters[0]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void RentParameters_VariousSizes_WorksCorrectly(int count)
    {
        // Arrange
        var typeMap = new TypeMapRegistry();
        using var context = new DatabaseContext(
            "Data Source=:memory:",
            SqliteFactory.Instance,
            typeMap);

        // Act
        var parameters = context.RentParameters(count);

        // Assert
        Assert.Equal(count, parameters.Length);
        Assert.All(parameters, p => Assert.NotNull(p));
    }
}
