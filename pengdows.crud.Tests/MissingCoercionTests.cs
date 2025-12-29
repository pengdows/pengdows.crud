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

        Assert.Throws<InvalidCastException>(() => coercion.TryRead(dbValue, out var _));
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

    [Fact]
    public void BooleanCoercion_TryWrite_True_SetsCorrectly()
    {
        var coercion = new BooleanCoercion();
        var parameter = new TestDbParameter();

        var success = coercion.TryWrite(true, parameter);

        Assert.True(success);
        Assert.Equal(true, parameter.Value);
        Assert.Equal(DbType.Boolean, parameter.DbType);
    }

    #endregion

    #region DateTimeCoercion Tests

    [Fact]
    public void DateTimeCoercion_TryRead_DateTimeUtc_ReturnsUtc()
    {
        var coercion = new DateTimeCoercion();
        var utcTime = DateTime.UtcNow;
        var dbValue = new DbValue(utcTime);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void DateTimeCoercion_TryRead_DateTimeLocal_ConvertsToUtc()
    {
        var coercion = new DateTimeCoercion();
        var localTime = DateTime.Now;
        var dbValue = new DbValue(localTime);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void DateTimeCoercion_TryRead_DateTimeUnspecified_BecomesUtc()
    {
        var coercion = new DateTimeCoercion();
        var unspecifiedTime = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var dbValue = new DbValue(unspecifiedTime);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void DateTimeCoercion_TryRead_DateTimeOffset_ConvertsToUtc()
    {
        var coercion = new DateTimeCoercion();
        var dto = DateTimeOffset.Now;
        var dbValue = new DbValue(dto);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(dto.UtcDateTime.Date, result.Date);
    }

    [Fact]
    public void DateTimeCoercion_TryRead_StringDateTimeOffset_ConvertsToUtc()
    {
        var coercion = new DateTimeCoercion();
        var dbValue = new DbValue("2023-01-01T12:00:00+00:00");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void DateTimeCoercion_TryRead_StringDateTime_ConvertsToUtc()
    {
        var coercion = new DateTimeCoercion();
        var dbValue = new DbValue("2023-01-01T12:00:00");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void DateTimeCoercion_TryRead_NullValue_ReturnsFalse()
    {
        var coercion = new DateTimeCoercion();
        var dbValue = new DbValue(null);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(DateTime.MinValue, result);
    }

    [Fact]
    public void DateTimeCoercion_TryWrite_DateTime_SetsCorrectly()
    {
        var coercion = new DateTimeCoercion();
        var parameter = new TestDbParameter();
        var dateTime = DateTime.UtcNow;

        var success = coercion.TryWrite(dateTime, parameter);

        Assert.True(success);
        Assert.Equal(dateTime, parameter.Value);
        Assert.Equal(DbType.DateTime, parameter.DbType);
    }

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

    [Fact]
    public void DecimalCoercion_TryWrite_Decimal_SetsCorrectly()
    {
        var coercion = new DecimalCoercion();
        var parameter = new TestDbParameter();

        var success = coercion.TryWrite(123.45m, parameter);

        Assert.True(success);
        Assert.Equal(123.45m, parameter.Value);
        Assert.Equal(DbType.Decimal, parameter.DbType);
    }

    #endregion

    #region JsonDocumentCoercion Tests

    [Fact]
    public void JsonDocumentCoercion_TryRead_JsonDocument_ReturnsValue()
    {
        var coercion = new JsonDocumentCoercion();
        using var doc = JsonDocument.Parse("{\"test\":true}");
        var dbValue = new DbValue(doc);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void JsonDocumentCoercion_TryRead_JsonElement_ParsesCorrectly()
    {
        var coercion = new JsonDocumentCoercion();
        using var doc = JsonDocument.Parse("{\"test\":true}");
        var dbValue = new DbValue(doc.RootElement);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void JsonDocumentCoercion_TryRead_String_ParsesCorrectly()
    {
        var coercion = new JsonDocumentCoercion();
        var dbValue = new DbValue("{\"test\":true}");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void JsonDocumentCoercion_TryRead_ByteArray_ParsesCorrectly()
    {
        var coercion = new JsonDocumentCoercion();
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");
        var dbValue = new DbValue(bytes);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void JsonDocumentCoercion_TryRead_EmptyString_ReturnsFalse()
    {
        var coercion = new JsonDocumentCoercion();
        var dbValue = new DbValue("");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void JsonDocumentCoercion_TryRead_InvalidJson_ReturnsFalse()
    {
        var coercion = new JsonDocumentCoercion();
        var dbValue = new DbValue("{invalid}");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void JsonDocumentCoercion_TryRead_NullValue_ReturnsFalse()
    {
        var coercion = new JsonDocumentCoercion();
        var dbValue = new DbValue(null);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void JsonDocumentCoercion_TryWrite_JsonDocument_SetsCorrectly()
    {
        var coercion = new JsonDocumentCoercion();
        var parameter = new TestDbParameter();
        using var doc = JsonDocument.Parse("{\"test\":true}");

        var success = coercion.TryWrite(doc, parameter);

        Assert.True(success);
        Assert.NotNull(parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    [Fact]
    public void JsonDocumentCoercion_TryWrite_Null_SetsDbNull()
    {
        var coercion = new JsonDocumentCoercion();
        var parameter = new TestDbParameter();

        var success = coercion.TryWrite(null, parameter);

        Assert.True(success);
        Assert.Equal(DBNull.Value, parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
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
        Assert.NotEqual(default(JsonElement), result);
    }

    [Fact]
    public void JsonElementCoercion_TryRead_JsonDocument_ReturnsElement()
    {
        var coercion = new JsonElementCoercion();
        using var doc = JsonDocument.Parse("{\"test\":true}");
        var dbValue = new DbValue(doc);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default(JsonElement), result);
    }

    [Fact]
    public void JsonElementCoercion_TryRead_String_ParsesCorrectly()
    {
        var coercion = new JsonElementCoercion();
        var dbValue = new DbValue("{\"test\":true}");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default(JsonElement), result);
    }

    [Fact]
    public void JsonElementCoercion_TryRead_ByteArray_ParsesCorrectly()
    {
        var coercion = new JsonElementCoercion();
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");
        var dbValue = new DbValue(bytes);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default(JsonElement), result);
    }

    [Fact]
    public void JsonElementCoercion_TryRead_EmptyString_ReturnsFalse()
    {
        var coercion = new JsonElementCoercion();
        var dbValue = new DbValue("");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default(JsonElement), result);
    }

    [Fact]
    public void JsonElementCoercion_TryRead_InvalidJson_ReturnsFalse()
    {
        var coercion = new JsonElementCoercion();
        var dbValue = new DbValue("{invalid}");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default(JsonElement), result);
    }

    [Fact]
    public void JsonElementCoercion_TryRead_NullValue_ReturnsFalse()
    {
        var coercion = new JsonElementCoercion();
        var dbValue = new DbValue(null);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default(JsonElement), result);
    }

    [Fact]
    public void JsonElementCoercion_TryWrite_JsonElement_SetsCorrectly()
    {
        var coercion = new JsonElementCoercion();
        var parameter = new TestDbParameter();
        using var doc = JsonDocument.Parse("{\"test\":true}");

        var success = coercion.TryWrite(doc.RootElement, parameter);

        Assert.True(success);
        Assert.NotNull(parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    #endregion

    #region PostgreSqlIntervalCoercion Tests

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_PostgreSqlInterval_ReturnsValue()
    {
        var coercion = new PostgreSqlIntervalCoercion();
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
        var coercion = new PostgreSqlIntervalCoercion();
        var timeSpan = TimeSpan.FromHours(26.5); // 1 day, 2 hours, 30 minutes
        var dbValue = new DbValue(timeSpan);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default(PostgreSqlInterval), result);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_NullValue_ReturnsFalse()
    {
        var coercion = new PostgreSqlIntervalCoercion();
        var dbValue = new DbValue(null);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default(PostgreSqlInterval), result);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_InvalidType_ReturnsFalse()
    {
        var coercion = new PostgreSqlIntervalCoercion();
        var dbValue = new DbValue("invalid");

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default(PostgreSqlInterval), result);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryWrite_Interval_SetsCorrectly()
    {
        var coercion = new PostgreSqlIntervalCoercion();
        var parameter = new TestDbParameter();
        var interval = new PostgreSqlInterval(1, 2, 3600000000); // 1 month, 2 days, 1 hour in microseconds

        var success = coercion.TryWrite(interval, parameter);

        Assert.True(success);
        Assert.NotNull(parameter.Value);
        Assert.Equal(DbType.Object, parameter.DbType);
    }

    #endregion

    #region RowVersionValueCoercion Tests

    [Fact]
    public void RowVersionValueCoercion_TryRead_RowVersion_ReturnsValue()
    {
        var coercion = new RowVersionValueCoercion();
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
        var coercion = new RowVersionValueCoercion();
        var bytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 };
        var dbValue = new DbValue(bytes);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default(RowVersion), result);
    }

    [Fact]
    public void RowVersionValueCoercion_TryRead_ULong_ConvertsCorrectly()
    {
        var coercion = new RowVersionValueCoercion();
        var ulongValue = 12345UL;
        var dbValue = new DbValue(ulongValue);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.True(success);
        Assert.NotEqual(default(RowVersion), result);
    }

    [Fact]
    public void RowVersionValueCoercion_TryRead_WrongLengthArray_ReturnsFalse()
    {
        var coercion = new RowVersionValueCoercion();
        var bytes = new byte[] { 1, 2, 3 }; // Wrong length
        var dbValue = new DbValue(bytes);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default(RowVersion), result);
    }

    [Fact]
    public void RowVersionValueCoercion_TryRead_NullValue_ReturnsFalse()
    {
        var coercion = new RowVersionValueCoercion();
        var dbValue = new DbValue(null);

        var success = coercion.TryRead(dbValue, out var result);

        Assert.False(success);
        Assert.Equal(default(RowVersion), result);
    }

    [Fact]
    public void RowVersionValueCoercion_TryWrite_RowVersion_SetsCorrectly()
    {
        var coercion = new RowVersionValueCoercion();
        var parameter = new TestDbParameter();
        var bytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 };
        var rowVersion = new RowVersion(bytes);

        var success = coercion.TryWrite(rowVersion, parameter);

        Assert.True(success);
        Assert.Equal(bytes, parameter.Value);
        Assert.Equal(DbType.Binary, parameter.DbType);
        Assert.Equal(8, parameter.Size);
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
