using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Xunit;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for coercions that were missing coverage.
/// Ensures 90%+ coverage for all coercion types.
/// </summary>
public class MissingCoercionTests
{
    #region BooleanCoercion Tests

    [Fact]
    public void BooleanCoercion_TryRead_BoolValue_ReturnsTrue()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue(true);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.True(result);
    }

    [Fact]
    public void BooleanCoercion_TryRead_StringTrue_ReturnsTrue()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue("true");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.True(result);
    }

    [Fact]
    public void BooleanCoercion_TryRead_CharT_ReturnsTrue()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue('t');

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.True(result);
    }

    [Fact]
    public void BooleanCoercion_TryRead_CharY_ReturnsTrue()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue('y');

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.True(result);
    }

    [Fact]
    public void BooleanCoercion_TryRead_Char1_ReturnsTrue()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue('1');

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.True(result);
    }

    [Fact]
    public void BooleanCoercion_TryRead_CharF_ReturnsFalse()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue('f');

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.False(result);
    }

    [Fact]
    public void BooleanCoercion_TryRead_NumericNonZero_ReturnsTrue()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue(42);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.True(result);
    }

    [Fact]
    public void BooleanCoercion_TryRead_NumericZero_ReturnsFalse()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue(0);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.False(result);
    }

    [Fact]
    public void BooleanCoercion_TryRead_StringWithNumeric_ReturnsTrue()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue("42.5");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.True(result);
    }

    [Fact]
    public void BooleanCoercion_TryRead_Float_ReturnsTrue()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue(1.5f);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.True(result);
    }

    [Fact]
    public void BooleanCoercion_TryRead_Decimal_ReturnsTrue()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue(1.5m);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.True(result);
    }

    [Fact]
    public void BooleanCoercion_TryRead_InvalidChar_ThrowsException()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue('x');

        Assert.Throws<InvalidCastException>(() => coercion.TryRead(dbValue, out _));
    }

    [Fact]
    public void BooleanCoercion_TryRead_NullValue_ReturnsFalse()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue(null);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.False(result);
    }

    [Fact]
    public void BooleanCoercion_TryRead_InvalidStringValue_ReturnsFalse()
    {
        var coercion = new BooleanCoercion();
        var dbValue = new DbValue("invalid");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
    }

    #endregion

    #region DateTimeCoercion Tests

    #endregion

    #region DecimalCoercion Tests

    [Fact]
    public void DecimalCoercion_TryRead_DecimalValue_ReturnsValue()
    {
        var coercion = new DecimalCoercion();
        var dbValue = new DbValue(123.45m);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(123.45m, result);
    }

    [Fact]
    public void DecimalCoercion_TryRead_IntValue_ConvertsToDecimal()
    {
        var coercion = new DecimalCoercion();
        var dbValue = new DbValue(42);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(42m, result);
    }

    [Fact]
    public void DecimalCoercion_TryRead_DoubleValue_ConvertsToDecimal()
    {
        var coercion = new DecimalCoercion();
        var dbValue = new DbValue(123.45);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(123.45m, result);
    }

    [Fact]
    public void DecimalCoercion_TryRead_NullValue_ReturnsFalse()
    {
        var coercion = new DecimalCoercion();
        var dbValue = new DbValue(null);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(0m, result);
    }

    [Fact]
    public void DecimalCoercion_TryRead_InvalidValue_ReturnsFalse()
    {
        var coercion = new DecimalCoercion();
        var dbValue = new DbValue("invalid");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(0m, result);
    }

    #endregion

    #region ConverterRead<System.Text.Json.JsonDocument> Tests

    [Fact]
    public void JsonDocumentCoercion_TryRead_JsonDocument_ReturnsValue()
    {
        var coercion = new ConverterRead<System.Text.Json.JsonDocument>();
        using var doc = JsonDocument.Parse("{\"test\":true}");
        var dbValue = new DbValue(doc);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void JsonDocumentCoercion_TryRead_JsonElement_ParsesCorrectly()
    {
        var coercion = new ConverterRead<System.Text.Json.JsonDocument>();
        using var doc = JsonDocument.Parse("{\"test\":true}");
        var dbValue = new DbValue(doc.RootElement);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void JsonDocumentCoercion_TryRead_String_ParsesCorrectly()
    {
        var coercion = new ConverterRead<System.Text.Json.JsonDocument>();
        var dbValue = new DbValue("{\"test\":true}");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void JsonDocumentCoercion_TryRead_ByteArray_ParsesCorrectly()
    {
        var coercion = new ConverterRead<System.Text.Json.JsonDocument>();
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");
        var dbValue = new DbValue(bytes);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void JsonDocumentCoercion_TryRead_EmptyString_IsJsonNull()
    {
        // Blank text is the JSON null (COR-007, DRY-015).
        var coercion = new ConverterRead<System.Text.Json.JsonDocument>();
        var dbValue = new DbValue("");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, result!.RootElement.ValueKind);
    }

    [Fact]
    public void JsonDocumentCoercion_TryRead_InvalidJson_ReturnsFalse()
    {
        var coercion = new ConverterRead<System.Text.Json.JsonDocument>();
        var dbValue = new DbValue("{invalid}");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void JsonDocumentCoercion_TryRead_NullValue_ReturnsFalse()
    {
        var coercion = new ConverterRead<System.Text.Json.JsonDocument>();
        var dbValue = new DbValue(null);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    #endregion

    #region JsonElementCoercion Tests

    [Fact]
    public void JsonElementCoercion_TryRead_JsonElement_ReturnsValue()
    {
        var coercion = new JsonElementCoercion();
        using var doc = JsonDocument.Parse("{\"test\":true}");
        var dbValue = new DbValue(doc.RootElement);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default, result);
    }

    [Fact]
    public void JsonElementCoercion_TryRead_JsonDocument_ReturnsElement()
    {
        var coercion = new JsonElementCoercion();
        using var doc = JsonDocument.Parse("{\"test\":true}");
        var dbValue = new DbValue(doc);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default, result);
    }

    [Fact]
    public void JsonElementCoercion_TryRead_String_ParsesCorrectly()
    {
        var coercion = new JsonElementCoercion();
        var dbValue = new DbValue("{\"test\":true}");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default, result);
    }

    [Fact]
    public void JsonElementCoercion_TryRead_ByteArray_ParsesCorrectly()
    {
        var coercion = new JsonElementCoercion();
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");
        var dbValue = new DbValue(bytes);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default, result);
    }

    [Fact]
    public void JsonElementCoercion_TryRead_EmptyString_IsJsonNull()
    {
        // Blank text is the JSON null (COR-007, DRY-015).
        var coercion = new JsonElementCoercion();
        var dbValue = new DbValue("");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, result.ValueKind);
    }

    [Fact]
    public void JsonElementCoercion_TryRead_InvalidJson_ReturnsFalse()
    {
        var coercion = new JsonElementCoercion();
        var dbValue = new DbValue("{invalid}");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default, result);
    }

    [Fact]
    public void JsonElementCoercion_TryRead_NullValue_ReturnsFalse()
    {
        var coercion = new JsonElementCoercion();
        var dbValue = new DbValue(null);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default, result);
    }

    #endregion

    #region ConverterRead<PostgreSqlInterval> Tests

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_PostgreSqlInterval_ReturnsValue()
    {
        var coercion = new ConverterRead<PostgreSqlInterval>();
        var interval = new PostgreSqlInterval(1, 2, 3600000000); // 1 month, 2 days, 1 hour in microseconds
        var dbValue = new DbValue(interval);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(interval.Days, result.Days);
        Assert.Equal(interval.Months, result.Months);
        Assert.Equal(interval.Microseconds, result.Microseconds);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_TimeSpan_ConvertsCorrectly()
    {
        var coercion = new ConverterRead<PostgreSqlInterval>();
        var timeSpan = TimeSpan.FromHours(26.5); // 1 day, 2 hours, 30 minutes
        var dbValue = new DbValue(timeSpan);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default, result);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_NullValue_ReturnsFalse()
    {
        var coercion = new ConverterRead<PostgreSqlInterval>();
        var dbValue = new DbValue(null);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default, result);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_InvalidType_ReturnsFalse()
    {
        var coercion = new ConverterRead<PostgreSqlInterval>();
        var dbValue = new DbValue("invalid");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default, result);
    }

    #endregion

    #region ConverterRead<RowVersion> Tests

    [Fact]
    public void RowVersionValueCoercion_TryRead_RowVersion_ReturnsValue()
    {
        var coercion = new ConverterRead<RowVersion>();
        var bytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 };
        var rowVersion = new RowVersion(bytes);
        var dbValue = new DbValue(rowVersion);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(rowVersion, result);
    }

    [Fact]
    public void RowVersionValueCoercion_TryRead_8ByteArray_ConvertsCorrectly()
    {
        var coercion = new ConverterRead<RowVersion>();
        var bytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 };
        var dbValue = new DbValue(bytes);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default, result);
    }

    [Fact]
    public void RowVersionValueCoercion_TryRead_ULong_ConvertsCorrectly()
    {
        var coercion = new ConverterRead<RowVersion>();
        var ulongValue = 12345UL;
        var dbValue = new DbValue(ulongValue);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default, result);
    }

    [Fact]
    public void RowVersionValueCoercion_TryRead_WrongLengthArray_ReturnsFalse()
    {
        var coercion = new ConverterRead<RowVersion>();
        var bytes = new byte[] { 1, 2, 3 }; // Wrong length
        var dbValue = new DbValue(bytes);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default, result);
    }

    [Fact]
    public void RowVersionValueCoercion_TryRead_NullValue_ReturnsFalse()
    {
        var coercion = new ConverterRead<RowVersion>();
        var dbValue = new DbValue(null);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default, result);
    }

    #endregion
}