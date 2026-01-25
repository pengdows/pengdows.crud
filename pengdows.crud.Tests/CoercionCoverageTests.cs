using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Xunit;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests to improve coverage of coercion classes.
/// Targets uncovered lines in JsonValueCoercion, IntRangeCoercion, DateTimeRangeCoercion.
/// </summary>
public class CoercionCoverageTests
{
    #region JsonValueCoercion Tests

    [Fact]
    public void JsonValueCoercion_TryRead_WithJsonDocument_ReturnsTrue()
    {
        // Arrange
        var coercion = new JsonValueCoercion();
        using var doc = JsonDocument.Parse("{\"test\":true}");
        var dbValue = new DbValue(doc);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.True(success);
        Assert.NotEqual(default(JsonValue), result);
    }

    [Fact]
    public void JsonValueCoercion_TryRead_WithJsonElement_ReturnsTrue()
    {
        // Arrange
        var coercion = new JsonValueCoercion();
        using var doc = JsonDocument.Parse("{\"test\":true}");
        var dbValue = new DbValue(doc.RootElement);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.True(success);
        Assert.NotEqual(default(JsonValue), result);
    }

    [Fact]
    public void JsonValueCoercion_TryRead_WithValidString_ReturnsTrue()
    {
        // Arrange
        var coercion = new JsonValueCoercion();
        var jsonString = "{\"key\":\"value\"}";
        var dbValue = new DbValue(jsonString);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.True(success);
        Assert.Equal(jsonString, result.AsString());
    }

    [Fact]
    public void JsonValueCoercion_TryRead_WithInvalidJsonString_ReturnsFalse()
    {
        // Arrange
        var coercion = new JsonValueCoercion();
        var invalidJson = "{invalid json}";
        var dbValue = new DbValue(invalidJson);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(default(JsonValue), result);
    }

    [Fact]
    public void JsonValueCoercion_TryRead_WithNullValue_ReturnsFalse()
    {
        // Arrange
        var coercion = new JsonValueCoercion();
        var dbValue = new DbValue(null);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(default(JsonValue), result);
    }

    [Fact]
    public void JsonValueCoercion_TryRead_WithInvalidType_ReturnsFalse()
    {
        // Arrange
        var coercion = new JsonValueCoercion();
        var dbValue = new DbValue(12345);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(default(JsonValue), result);
    }

    [Fact]
    public void JsonValueCoercion_TryWrite_SetsCorrectly()
    {
        // Arrange
        var coercion = new JsonValueCoercion();
        var parameter = new TestDbParameter();
        var jsonValue = new JsonValue("{\"test\":true}");

        // Act
        var success = coercion.TryWrite(jsonValue, parameter);

        // Assert
        Assert.True(success);
        Assert.Equal("{\"test\":true}", parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    #endregion

    #region IntRangeCoercion Tests

    [Fact]
    public void IntRangeCoercion_TryRead_WithValidString_ReturnsTrue()
    {
        // Arrange
        var coercion = new IntRangeCoercion();
        var rangeString = "[1,10)";
        var dbValue = new DbValue(rangeString);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.True(success);
        Assert.NotEqual(default(Range<int>), result);
    }

    [Fact]
    public void IntRangeCoercion_TryRead_WithInvalidString_ReturnsFalse()
    {
        // Arrange
        var coercion = new IntRangeCoercion();
        var invalidRange = "invalid";
        var dbValue = new DbValue(invalidRange);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(default(Range<int>), result);
    }

    [Fact]
    public void IntRangeCoercion_TryRead_WithNullValue_ReturnsFalse()
    {
        // Arrange
        var coercion = new IntRangeCoercion();
        var dbValue = new DbValue(null);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(default(Range<int>), result);
    }

    [Fact]
    public void IntRangeCoercion_TryRead_WithInvalidType_ReturnsFalse()
    {
        // Arrange
        var coercion = new IntRangeCoercion();
        var dbValue = new DbValue(12345);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(default(Range<int>), result);
    }

    [Fact]
    public void IntRangeCoercion_TryWrite_SetsCorrectly()
    {
        // Arrange
        var coercion = new IntRangeCoercion();
        var parameter = new TestDbParameter();
        var range = new Range<int>(1, 10, isLowerInclusive: true, isUpperInclusive: false);

        // Act
        var success = coercion.TryWrite(range, parameter);

        // Assert
        Assert.True(success);
        Assert.NotNull(parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    #endregion

    #region DateTimeRangeCoercion Tests

    [Fact]
    public void DateTimeRangeCoercion_TryRead_WithValidString_ReturnsTrue()
    {
        // Arrange
        var coercion = new DateTimeRangeCoercion();
        var rangeString = "[2024-01-01,2024-12-31)";
        var dbValue = new DbValue(rangeString);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.True(success);
        Assert.NotEqual(default(Range<DateTime>), result);
    }

    [Fact]
    public void DateTimeRangeCoercion_TryRead_WithInvalidString_ReturnsFalse()
    {
        // Arrange
        var coercion = new DateTimeRangeCoercion();
        var invalidRange = "invalid";
        var dbValue = new DbValue(invalidRange);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(default(Range<DateTime>), result);
    }

    [Fact]
    public void DateTimeRangeCoercion_TryRead_WithNullValue_ReturnsFalse()
    {
        // Arrange
        var coercion = new DateTimeRangeCoercion();
        var dbValue = new DbValue(null);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(default(Range<DateTime>), result);
    }

    [Fact]
    public void DateTimeRangeCoercion_TryRead_WithInvalidType_ReturnsFalse()
    {
        // Arrange
        var coercion = new DateTimeRangeCoercion();
        var dbValue = new DbValue(12345);

        // Act
        var success = coercion.TryRead(dbValue, out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(default(Range<DateTime>), result);
    }

    [Fact]
    public void DateTimeRangeCoercion_TryWrite_SetsCorrectly()
    {
        // Arrange
        var coercion = new DateTimeRangeCoercion();
        var parameter = new TestDbParameter();
        var start = new DateTime(2024, 1, 1);
        var end = new DateTime(2024, 12, 31);
        var range = new Range<DateTime>(start, end, isLowerInclusive: true, isUpperInclusive: false);

        // Act
        var success = coercion.TryWrite(range, parameter);

        // Assert
        Assert.True(success);
        Assert.NotNull(parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    #endregion

    private class TestDbParameter : DbParameter
    {
        private string _parameterName = string.Empty;
        private string _sourceColumn = string.Empty;

        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName
        {
            get => _parameterName;
            set => _parameterName = value ?? string.Empty;
        }
        public override int Size { get; set; }
        [AllowNull]
        public override string SourceColumn
        {
            get => _sourceColumn;
            set => _sourceColumn = value ?? string.Empty;
        }
        public override bool SourceColumnNullMapping { get; set; }
        [AllowNull]
        public override object Value { get; set; } = DBNull.Value;

        public override void ResetDbType()
        {
            DbType = DbType.Object;
        }
    }
}
