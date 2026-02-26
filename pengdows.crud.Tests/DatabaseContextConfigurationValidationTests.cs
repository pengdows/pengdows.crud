using System;
using pengdows.crud.configuration;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Validates that MaxConcurrentWrites/Reads reject negative values.
/// Zero is valid — it creates a forbidden governor that throws on any acquire attempt,
/// which is used to disable writes on a ReadOnly context or reads on a WriteOnly context.
/// </summary>
public class DatabaseContextConfigurationValidationTests
{
    // -------------------------------------------------------------------------
    // MaxConcurrentWrites
    // -------------------------------------------------------------------------

    [Fact]
    public void MaxConcurrentWrites_Zero_SetsValue()
    {
        // 0 is valid — means "forbidden pool" (no write connections permitted)
        var config = new DatabaseContextConfiguration();
        config.MaxConcurrentWrites = 0;
        Assert.Equal(0, config.MaxConcurrentWrites);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void MaxConcurrentWrites_Negative_ThrowsArgumentOutOfRangeException(int value)
    {
        var config = new DatabaseContextConfiguration();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.MaxConcurrentWrites = value);
        Assert.Equal("value", ex.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void MaxConcurrentWrites_Positive_SetsValue(int value)
    {
        var config = new DatabaseContextConfiguration();
        config.MaxConcurrentWrites = value;
        Assert.Equal(value, config.MaxConcurrentWrites);
    }

    [Fact]
    public void MaxConcurrentWrites_Null_ClearsLimit()
    {
        var config = new DatabaseContextConfiguration { MaxConcurrentWrites = 5 };
        config.MaxConcurrentWrites = null;
        Assert.Null(config.MaxConcurrentWrites);
    }

    // -------------------------------------------------------------------------
    // MaxConcurrentReads
    // -------------------------------------------------------------------------

    [Fact]
    public void MaxConcurrentReads_Zero_SetsValue()
    {
        // 0 is valid — means "forbidden pool" (no read connections permitted)
        var config = new DatabaseContextConfiguration();
        config.MaxConcurrentReads = 0;
        Assert.Equal(0, config.MaxConcurrentReads);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void MaxConcurrentReads_Negative_ThrowsArgumentOutOfRangeException(int value)
    {
        var config = new DatabaseContextConfiguration();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.MaxConcurrentReads = value);
        Assert.Equal("value", ex.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void MaxConcurrentReads_Positive_SetsValue(int value)
    {
        var config = new DatabaseContextConfiguration();
        config.MaxConcurrentReads = value;
        Assert.Equal(value, config.MaxConcurrentReads);
    }

    [Fact]
    public void MaxConcurrentReads_Null_ClearsLimit()
    {
        var config = new DatabaseContextConfiguration { MaxConcurrentReads = 5 };
        config.MaxConcurrentReads = null;
        Assert.Null(config.MaxConcurrentReads);
    }
}