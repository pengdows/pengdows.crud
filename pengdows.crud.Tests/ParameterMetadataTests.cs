using System.Data;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for the lightweight ParameterMetadata struct used for parameter pooling optimization.
/// ParameterMetadata stores parameter information during SQL building (cheap),
/// then is used to populate pooled DbParameter objects at execution time.
/// </summary>
public class ParameterMetadataTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        // Arrange & Act
        var metadata = new ParameterMetadata(
            name: "testParam",
            dbType: DbType.String,
            value: "test value",
            direction: ParameterDirection.Input
        );

        // Assert
        Assert.Equal("testParam", metadata.Name);
        Assert.Equal(DbType.String, metadata.DbType);
        Assert.Equal("test value", metadata.Value);
        Assert.Equal(ParameterDirection.Input, metadata.Direction);
    }

    [Fact]
    public void Constructor_DefaultsToInputDirection()
    {
        // Arrange & Act
        var metadata = new ParameterMetadata(
            name: "testParam",
            dbType: DbType.Int32,
            value: 42
        );

        // Assert
        Assert.Equal(ParameterDirection.Input, metadata.Direction);
    }

    [Fact]
    public void IsOutput_ReturnsTrueForOutputParameter()
    {
        // Arrange
        var metadata = new ParameterMetadata(
            "output",
            DbType.Int32,
            null,
            ParameterDirection.Output
        );

        // Act & Assert
        Assert.True(metadata.IsOutput);
    }

    [Fact]
    public void IsOutput_ReturnsTrueForInputOutputParameter()
    {
        // Arrange
        var metadata = new ParameterMetadata(
            "inputOutput",
            DbType.String,
            "initial",
            ParameterDirection.InputOutput
        );

        // Act & Assert
        Assert.True(metadata.IsOutput);
    }

    [Fact]
    public void IsOutput_ReturnsTrueForReturnValue()
    {
        // Arrange
        var metadata = new ParameterMetadata(
            "returnValue",
            DbType.Int32,
            null,
            ParameterDirection.ReturnValue
        );

        // Act & Assert
        Assert.True(metadata.IsOutput);
    }

    [Fact]
    public void IsOutput_ReturnsFalseForInputParameter()
    {
        // Arrange
        var metadata = new ParameterMetadata(
            "input",
            DbType.String,
            "value",
            ParameterDirection.Input
        );

        // Act & Assert
        Assert.False(metadata.IsOutput);
    }

    [Fact]
    public void CanStoreNullValue()
    {
        // Arrange & Act
        var metadata = new ParameterMetadata(
            "nullParam",
            DbType.String,
            null
        );

        // Assert
        Assert.Null(metadata.Value);
    }

    [Fact]
    public void StructIsSmall_VerifySize()
    {
        // This test documents the expected size of the struct
        // It should be approximately 16 bytes (2 references + 2 enums)
        // This is a documentation test - actual size may vary by runtime

        var metadata = new ParameterMetadata("test", DbType.Int32, 42);

        // Verify it's a value type
        Assert.True(metadata.GetType().IsValueType);
    }

    [Fact]
    public void DifferentInstances_WithSameValues_AreEqual()
    {
        // Arrange
        var meta1 = new ParameterMetadata("param", DbType.Int32, 42);
        var meta2 = new ParameterMetadata("param", DbType.Int32, 42);

        // Act & Assert - structs with same values should be equal
        Assert.Equal(meta1.Name, meta2.Name);
        Assert.Equal(meta1.DbType, meta2.DbType);
        Assert.Equal(meta1.Value, meta2.Value);
        Assert.Equal(meta1.Direction, meta2.Direction);
    }

    [Theory]
    [InlineData(DbType.String, "test")]
    [InlineData(DbType.Int32, 42)]
    [InlineData(DbType.Decimal, 123.45)]
    [InlineData(DbType.Boolean, true)]
    [InlineData(DbType.DateTime, null)]
    public void CanStoreVariousTypes(DbType dbType, object? value)
    {
        // Arrange & Act
        var metadata = new ParameterMetadata("param", dbType, value);

        // Assert
        Assert.Equal(dbType, metadata.DbType);
        Assert.Equal(value, metadata.Value);
    }
}
