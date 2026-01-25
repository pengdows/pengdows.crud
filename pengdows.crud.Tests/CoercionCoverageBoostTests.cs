using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Xunit;
using Moq;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests designed to boost coverage of low-coverage coercion classes.
/// Targets: DateTimeRangeCoercion (20%), IntRangeCoercion (33.3%), DbCoercion<T> (66.6%), 
/// HStoreCoercion (53.3%), IntArrayCoercion (57.1%), StringArrayCoercion (53.8%), TimeSpanCoercion (56.2%).
/// Uses the correct TryRead/TryWrite API.
/// </summary>
public class CoercionCoverageBoostTests
{
    [Fact]
    public void DateTimeRangeCoercion_TryRead_ValidRangeString_ReturnsTrue()
    {
        // Arrange
        var coercion = new DateTimeRangeCoercion();
        var dbValue = new DbValue("[2023-01-01,2023-01-02)", typeof(string));

        // Act
        var result = coercion.TryRead(dbValue, out var range);

        // Assert
        Assert.True(result);
        // Verify range parsing worked (exact properties depend on Range<T> implementation)
        Assert.NotEqual(default, range);
    }

    [Fact]
    public void DateTimeRangeCoercion_TryRead_NullValue_ReturnsFalse()
    {
        // Arrange
        var coercion = new DateTimeRangeCoercion();
        var dbValue = new DbValue(null);

        // Act
        var result = coercion.TryRead(dbValue, out var range);

        // Assert
        Assert.False(result);
        Assert.Equal(default, range);
    }

    [Fact]
    public void DateTimeRangeCoercion_TryRead_InvalidString_ReturnsFalse()
    {
        // Arrange
        var coercion = new DateTimeRangeCoercion();
        var dbValue = new DbValue("invalid range format", typeof(string));

        // Act
        var result = coercion.TryRead(dbValue, out var range);

        // Assert
        Assert.False(result);
        Assert.Equal(default, range);
    }

    [Fact]
    public void DateTimeRangeCoercion_TryWrite_ValidRange_ReturnsTrue()
    {
        // Arrange
        var coercion = new DateTimeRangeCoercion();
        var mockParam = new Mock<DbParameter>();
        var range = Range<DateTime>.Parse("[2023-01-01,2023-01-02)");

        // Act
        var result = coercion.TryWrite(range, mockParam.Object);

        // Assert
        Assert.True(result);
        mockParam.VerifySet(p => p.Value = It.IsAny<string>(), Times.Once);
        mockParam.VerifySet(p => p.DbType = DbType.String, Times.Once);
    }

    [Fact]
    public void IntRangeCoercion_TryRead_ValidRangeString_ReturnsTrue()
    {
        // Arrange
        var coercion = new IntRangeCoercion();
        var dbValue = new DbValue("[1,10)", typeof(string));

        // Act
        var result = coercion.TryRead(dbValue, out var range);

        // Assert
        Assert.True(result);
        Assert.NotEqual(default, range);
    }

    [Fact]
    public void IntRangeCoercion_TryRead_NullValue_ReturnsFalse()
    {
        // Arrange
        var coercion = new IntRangeCoercion();
        var dbValue = new DbValue(null);

        // Act
        var result = coercion.TryRead(dbValue, out var range);

        // Assert
        Assert.False(result);
        Assert.Equal(default, range);
    }

    [Fact]
    public void IntRangeCoercion_TryWrite_ValidRange_ReturnsTrue()
    {
        // Arrange
        var coercion = new IntRangeCoercion();
        var mockParam = new Mock<DbParameter>();
        var range = Range<int>.Parse("[1,10)");

        // Act
        var result = coercion.TryWrite(range, mockParam.Object);

        // Assert
        Assert.True(result);
        mockParam.VerifySet(p => p.Value = It.IsAny<string>(), Times.Once);
        mockParam.VerifySet(p => p.DbType = DbType.String, Times.Once);
    }

    [Fact]
    public void DbCoercion_TargetType_ReturnsCorrectType()
    {
        // Arrange
        var coercion = new TestDbCoercion();

        // Act
        var targetType = coercion.TargetType;

        // Assert
        Assert.Equal(typeof(TestType), targetType);
    }

    [Fact]
    public void DbCoercion_TryRead_ValidValue_ReturnsTrue()
    {
        // Arrange
        var coercion = new TestDbCoercion();
        var dbValue = new DbValue("test", typeof(string));

        // Act
        var result = coercion.TryRead(dbValue, out var value);

        // Assert
        Assert.True(result);
        Assert.NotNull(value);
        Assert.Equal("test", value.Value);
    }

    [Fact]
    public void DbCoercion_TryRead_NullValue_ReturnsTrue()
    {
        // Arrange
        var coercion = new TestDbCoercion();
        var dbValue = new DbValue(null);

        // Act
        var result = coercion.TryRead(dbValue, out var value);

        // Assert
        Assert.True(result);
        Assert.Null(value);
    }

    [Fact]
    public void DbCoercion_TryWrite_ValidValue_ReturnsTrue()
    {
        // Arrange
        var coercion = new TestDbCoercion();
        var mockParam = new Mock<DbParameter>();
        var testValue = new TestType { Value = "test" };

        // Act
        var result = coercion.TryWrite(testValue, mockParam.Object);

        // Assert
        Assert.True(result);
        mockParam.VerifySet(p => p.Value = "test", Times.Once);
    }

    [Fact]
    public void DbCoercion_TryWrite_NullValue_ReturnsTrue()
    {
        // Arrange
        var coercion = new TestDbCoercion();
        var mockParam = new Mock<DbParameter>();

        // Act
        var result = coercion.TryWrite(null, mockParam.Object);

        // Assert
        Assert.True(result);
        mockParam.VerifySet(p => p.Value = DBNull.Value, Times.Once);
    }

    [Fact]
    public void HStoreCoercion_TryRead_ValidHStoreString_ReturnsTrue()
    {
        // Arrange
        var coercion = new HStoreCoercion();
        var dbValue = new DbValue("\"key1\"=>\"value1\",\"key2\"=>\"value2\"", typeof(string));

        // Act
        var result = coercion.TryRead(dbValue, out var hstore);

        // Assert
        Assert.True(result);
        Assert.Equal("value1", hstore["key1"]);
        Assert.Equal("value2", hstore["key2"]);
    }

    [Fact]
    public void HStoreCoercion_TryRead_NullValue_ReturnsFalse()
    {
        // Arrange
        var coercion = new HStoreCoercion();
        var dbValue = new DbValue(null);

        // Act
        var result = coercion.TryRead(dbValue, out var hstore);

        // Assert
        Assert.False(result);
        Assert.Equal(default, hstore);
    }

    [Fact]
    public void HStoreCoercion_TryRead_InvalidString_ReturnsFalse()
    {
        // Arrange
        var coercion = new HStoreCoercion();
        var dbValue = new DbValue("invalid hstore format", typeof(string));

        // Act
        var result = coercion.TryRead(dbValue, out var hstore);

        // Assert
        Assert.False(result);
        Assert.Equal(default, hstore);
    }

    [Fact]
    public void HStoreCoercion_TryWrite_ValidHStore_ReturnsTrue()
    {
        // Arrange
        var coercion = new HStoreCoercion();
        var mockParam = new Mock<DbParameter>();
        var hstore = new HStore(new Dictionary<string, string?> { { "key1", "value1" } });

        // Act
        var result = coercion.TryWrite(hstore, mockParam.Object);

        // Assert
        Assert.True(result);
        mockParam.VerifySet(p => p.Value = It.IsAny<string>(), Times.Once);
    }

    [Fact]
    public void IntArrayCoercion_TryRead_ValidArrayString_ReturnsTrue()
    {
        // Arrange
        var coercion = new IntArrayCoercion();
        var dbValue = new DbValue("1,2,3,4,5", typeof(string));

        // Act
        var result = coercion.TryRead(dbValue, out var intArray);

        // Assert
        Assert.True(result);
        Assert.NotNull(intArray);
        Assert.Equal(5, intArray.Length);
        Assert.Equal(new int[] { 1, 2, 3, 4, 5 }, intArray);
    }

    [Fact]
    public void IntArrayCoercion_TryRead_EmptyArrayString_ReturnsTrue()
    {
        // Arrange
        var coercion = new IntArrayCoercion();
        var dbValue = new DbValue("", typeof(string));

        // Act
        var result = coercion.TryRead(dbValue, out var intArray);

        // Assert
        Assert.True(result);
        Assert.NotNull(intArray);
        Assert.Empty(intArray);
    }

    [Fact]
    public void IntArrayCoercion_TryRead_IntArrayValue_ReturnsTrue()
    {
        // Arrange
        var coercion = new IntArrayCoercion();
        var intArray = new int[] { 1, 2, 3, 4, 5 };
        var dbValue = new DbValue(intArray, typeof(int[]));

        // Act
        var result = coercion.TryRead(dbValue, out var resultArray);

        // Assert
        Assert.True(result);
        Assert.NotNull(resultArray);
        Assert.Equal(intArray, resultArray);
    }

    [Fact]
    public void IntArrayCoercion_TryRead_InvalidString_ReturnsFalse()
    {
        // Arrange
        var coercion = new IntArrayCoercion();
        var dbValue = new DbValue("not,valid,integers", typeof(string));

        // Act
        var result = coercion.TryRead(dbValue, out var intArray);

        // Assert
        Assert.False(result);
        Assert.Null(intArray);
    }

    [Fact]
    public void IntArrayCoercion_TryRead_NullValue_ReturnsFalse()
    {
        // Arrange
        var coercion = new IntArrayCoercion();
        var dbValue = new DbValue(null);

        // Act
        var result = coercion.TryRead(dbValue, out var intArray);

        // Assert
        Assert.False(result);
        Assert.Null(intArray);
    }

    [Fact]
    public void IntArrayCoercion_TryWrite_ValidArray_ReturnsTrue()
    {
        // Arrange
        var coercion = new IntArrayCoercion();
        var mockParam = new Mock<DbParameter>();
        var intArray = new int[] { 1, 2, 3, 4, 5 };

        // Act
        var result = coercion.TryWrite(intArray, mockParam.Object);

        // Assert
        Assert.True(result);
        mockParam.VerifySet(p => p.Value = It.IsAny<object>(), Times.Once);
    }

    [Fact]
    public void StringArrayCoercion_TryRead_ValidArrayValue_ReturnsTrue()
    {
        // Arrange
        var coercion = new StringArrayCoercion();
        var stringArray = new string[] { "hello", "world", "test" };
        var dbValue = new DbValue(stringArray, typeof(string[]));

        // Act
        var result = coercion.TryRead(dbValue, out var resultArray);

        // Assert
        Assert.True(result);
        Assert.NotNull(resultArray);
        Assert.Equal(stringArray, resultArray);
    }

    [Fact]
    public void StringArrayCoercion_TryRead_NonArrayValue_ReturnsFalse()
    {
        // Arrange
        var coercion = new StringArrayCoercion();
        var dbValue = new DbValue("not an array", typeof(string));

        // Act
        var result = coercion.TryRead(dbValue, out var stringArray);

        // Assert
        Assert.False(result);
        Assert.Null(stringArray);
    }

    [Fact]
    public void StringArrayCoercion_TryRead_WithNullValues_ReturnsTrue()
    {
        // Arrange
        var coercion = new StringArrayCoercion();
        var stringArray = new string?[] { "hello", null, "world" };
        var dbValue = new DbValue(stringArray, typeof(string[]));

        // Act
        var result = coercion.TryRead(dbValue, out var resultArray);

        // Assert
        Assert.True(result);
        Assert.NotNull(resultArray);
        Assert.Equal(3, resultArray.Length);
        Assert.Equal("hello", resultArray[0]);
        Assert.Null(resultArray[1]);
        Assert.Equal("world", resultArray[2]);
    }

    [Fact]
    public void StringArrayCoercion_TryWrite_ValidArray_ReturnsTrue()
    {
        // Arrange
        var coercion = new StringArrayCoercion();
        var mockParam = new Mock<DbParameter>();
        var stringArray = new string[] { "hello", "world", "test" };

        // Act
        var result = coercion.TryWrite(stringArray, mockParam.Object);

        // Assert
        Assert.True(result);
        mockParam.VerifySet(p => p.Value = It.IsAny<object>(), Times.Once);
    }

    [Fact]
    public void TimeSpanCoercion_TryRead_ValidTimeSpanString_ReturnsTrue()
    {
        // Arrange
        var coercion = new TimeSpanCoercion();
        var dbValue = new DbValue("1.02:03:04", typeof(string));

        // Act
        var result = coercion.TryRead(dbValue, out var timespan);

        // Assert
        Assert.True(result);
        Assert.Equal(new TimeSpan(1, 2, 3, 4), timespan);
    }

    [Fact]
    public void TimeSpanCoercion_TryRead_NullValue_ReturnsFalse()
    {
        // Arrange
        var coercion = new TimeSpanCoercion();
        var dbValue = new DbValue(null);

        // Act
        var result = coercion.TryRead(dbValue, out var timespan);

        // Assert
        Assert.False(result);
        Assert.Equal(default, timespan);
    }

    [Fact]
    public void TimeSpanCoercion_TryRead_InvalidFormat_ReturnsFalse()
    {
        // Arrange
        var coercion = new TimeSpanCoercion();
        var dbValue = new DbValue("not a timespan", typeof(string));

        // Act
        var result = coercion.TryRead(dbValue, out var timespan);

        // Assert
        Assert.False(result);
        Assert.Equal(default, timespan);
    }

    [Fact]
    public void TimeSpanCoercion_TryWrite_ValidTimeSpan_ReturnsTrue()
    {
        // Arrange
        var coercion = new TimeSpanCoercion();
        var mockParam = new Mock<DbParameter>();
        var timespan = new TimeSpan(1, 2, 3, 4);

        // Act
        var result = coercion.TryWrite(timespan, mockParam.Object);

        // Assert
        Assert.True(result);
        mockParam.VerifySet(p => p.Value = It.IsAny<object>(), Times.Once);
    }

    // Test implementation of DbCoercion<T> for testing the base class
    private class TestDbCoercion : DbCoercion<TestType>
    {
        public override bool TryRead(in DbValue src, out TestType? value)
        {
            if (src.IsNull)
            {
                value = null;
                return true;
            }

            value = new TestType { Value = src.RawValue?.ToString() ?? "" };
            return true;
        }

        public override bool TryWrite(TestType? value, DbParameter parameter)
        {
            parameter.Value = value?.Value ?? (object)DBNull.Value;
            return true;
        }
    }

    private class TestType
    {
        public string Value { get; set; } = "";
    }
}