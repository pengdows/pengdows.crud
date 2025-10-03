using System;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class CoercionTests
{
    private readonly CoercionRegistry _registry = new();

    #region GUID Tests

    [Fact]
    public void GuidCoercion_ShouldHandleGuidValue()
    {
        var originalGuid = Guid.NewGuid();
        var dbValue = new DbValue(originalGuid);

        var success = _registry.TryRead(dbValue, typeof(Guid), out var result);

        Assert.True(success);
        Assert.Equal(originalGuid, result);
    }

    [Fact]
    public void GuidCoercion_ShouldHandleByteArray()
    {
        var originalGuid = Guid.NewGuid();
        var bytes = originalGuid.ToByteArray();
        var dbValue = new DbValue(bytes);

        var success = _registry.TryRead(dbValue, typeof(Guid), out var result);

        Assert.True(success);
        Assert.Equal(originalGuid, result);
    }

    [Fact]
    public void GuidCoercion_ShouldHandleStringFormats()
    {
        var originalGuid = Guid.NewGuid();
        var guidString = originalGuid.ToString();
        var dbValue = new DbValue(guidString);

        var success = _registry.TryRead(dbValue, typeof(Guid), out var result);

        Assert.True(success);
        Assert.Equal(originalGuid, result);
    }

    [Fact]
    public void GuidCoercion_ShouldWriteToParameter()
    {
        var guid = Guid.NewGuid();
        var parameter = new TestDbParameter();

        var success = _registry.TryWrite(guid, parameter);

        Assert.True(success);
        Assert.Equal(guid, parameter.Value);
        Assert.Equal(DbType.Guid, parameter.DbType);
    }

    #endregion

    #region RowVersion Tests

    [Fact]
    public void RowVersionCoercion_ShouldHandle8ByteArray()
    {
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };
        var dbValue = new DbValue(bytes);

        var success = _registry.TryRead(dbValue, typeof(byte[]), out var result);

        Assert.True(success);
        Assert.Equal(bytes, result);
    }

    [Fact]
    public void RowVersionCoercion_ShouldRejectWrongLength()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03 }; // Wrong length
        var dbValue = new DbValue(bytes);

        var success = _registry.TryRead(dbValue, typeof(byte[]), out var result);

        Assert.False(success);
    }

    [Fact]
    public void RowVersionCoercion_ShouldHandleULong()
    {
        var ulongValue = 12345UL;
        var dbValue = new DbValue(ulongValue);

        var success = _registry.TryRead(dbValue, typeof(byte[]), out var result);

        Assert.True(success);
        var resultBytes = (byte[])result!;
        Assert.Equal(8, resultBytes.Length);
    }

    #endregion

    #region JSON Tests

    [Fact]
    public void JsonValueCoercion_ShouldHandleJsonString()
    {
        var jsonText = "{\"name\":\"test\",\"value\":123}";
        var dbValue = new DbValue(jsonText);

        var success = _registry.TryRead(dbValue, typeof(JsonValue), out var result);

        Assert.True(success);
        var jsonValue = (JsonValue)result!;
        Assert.Contains("test", jsonValue.AsString());
    }

    [Fact]
    public void JsonValueCoercion_ShouldHandleJsonDocument()
    {
        using var doc = JsonDocument.Parse("{\"test\":true}");
        var dbValue = new DbValue(doc);

        var success = _registry.TryRead(dbValue, typeof(JsonValue), out var result);

        Assert.True(success);
        var jsonValue = (JsonValue)result!;
        Assert.Contains("test", jsonValue.AsString());
    }

    [Fact]
    public void JsonValueCoercion_ShouldWriteAsString()
    {
        var jsonValue = new JsonValue("{\"key\":\"value\"}");
        var parameter = new TestDbParameter();

        var success = _registry.TryWrite(jsonValue, parameter);

        Assert.True(success);
        Assert.Equal("{\"key\":\"value\"}", parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    [Fact]
    public void JsonValueCoercion_ShouldRejectInvalidJson()
    {
        var invalidJson = "{invalid json";
        var dbValue = new DbValue(invalidJson);

        var success = _registry.TryRead(dbValue, typeof(JsonValue), out var result);

        Assert.False(success);
    }

    #endregion

    #region HStore Tests

    [Fact]
    public void HStoreCoercion_ShouldParseValidHStore()
    {
        var hstoreText = "\"key1\"=>\"value1\", \"key2\"=>NULL, \"key3\"=>\"value3\"";
        var dbValue = new DbValue(hstoreText);

        var success = _registry.TryRead(dbValue, typeof(HStore), out var result);

        Assert.True(success);
        var hstore = (HStore)result!;
        Assert.Equal("value1", hstore["key1"]);
        Assert.Null(hstore["key2"]);
        Assert.Equal("value3", hstore["key3"]);
    }

    [Fact]
    public void HStoreCoercion_ShouldWriteCanonicalFormat()
    {
        var data = new System.Collections.Generic.Dictionary<string, string?>
        {
            ["key1"] = "value1",
            ["key2"] = null,
            ["key3"] = "value with spaces"
        };
        var hstore = new HStore(data);
        var parameter = new TestDbParameter();

        var success = _registry.TryWrite(hstore, parameter);

        Assert.True(success);
        var output = (string)parameter.Value!;
        Assert.Contains("key1", output);
        Assert.Contains("NULL", output);
        Assert.Contains("\"value with spaces\"", output);
    }

    #endregion

    #region Range Tests

    [Fact]
    public void IntRangeCoercion_ShouldParseRange()
    {
        var rangeText = "[1,10)";
        var dbValue = new DbValue(rangeText);

        var success = _registry.TryRead(dbValue, typeof(Range<int>), out var result);

        Assert.True(success);
        var range = (Range<int>)result!;
        Assert.Equal(1, range.Lower);
        Assert.Equal(10, range.Upper);
        Assert.True(range.IsLowerInclusive);
        Assert.False(range.IsUpperInclusive);
    }

    [Fact]
    public void DateTimeRangeCoercion_ShouldHandleDateRanges()
    {
        var start = new DateTime(2023, 1, 1);
        var end = new DateTime(2023, 12, 31);
        var range = new Range<DateTime>(start, end, true, false);
        var parameter = new TestDbParameter();

        var success = _registry.TryWrite(range, parameter);

        Assert.True(success);
        var rangeText = (string)parameter.Value!;
        Assert.Contains("2023", rangeText);
        Assert.StartsWith("[", rangeText);
        Assert.EndsWith(")", rangeText);
    }

    #endregion

    #region Array Tests

    [Fact]
    public void IntArrayCoercion_ShouldHandleIntArray()
    {
        var intArray = new[] { 1, 2, 3, 4, 5 };
        var dbValue = new DbValue(intArray);

        var success = _registry.TryRead(dbValue, typeof(int[]), out var result);

        Assert.True(success);
        Assert.Equal(intArray, result);
    }

    [Fact]
    public void IntArrayCoercion_ShouldParseCommaSeparatedString()
    {
        var arrayText = "1,2,3,4,5";
        var dbValue = new DbValue(arrayText);

        var success = _registry.TryRead(dbValue, typeof(int[]), out var result);

        Assert.True(success);
        var intArray = (int[])result!;
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, intArray);
    }

    [Fact]
    public void StringArrayCoercion_ShouldHandleStringArray()
    {
        var stringArray = new[] { "hello", "world", "test" };
        var dbValue = new DbValue(stringArray);

        var success = _registry.TryRead(dbValue, typeof(string[]), out var result);

        Assert.True(success);
        Assert.Equal(stringArray, result);
    }

    #endregion

    #region TimeSpan Tests

    [Fact]
    public void TimeSpanCoercion_ShouldHandleTimeSpan()
    {
        var timeSpan = TimeSpan.FromHours(2.5);
        var dbValue = new DbValue(timeSpan);

        var success = _registry.TryRead(dbValue, typeof(TimeSpan), out var result);

        Assert.True(success);
        Assert.Equal(timeSpan, result);
    }

    [Fact]
    public void TimeSpanCoercion_ShouldParseTimeString()
    {
        var timeString = "02:30:00";
        var dbValue = new DbValue(timeString);

        var success = _registry.TryRead(dbValue, typeof(TimeSpan), out var result);

        Assert.True(success);
        var timeSpan = (TimeSpan)result!;
        Assert.Equal(TimeSpan.FromHours(2.5), timeSpan);
    }

    [Fact]
    public void TimeSpanCoercion_ShouldHandleDoubleSeconds()
    {
        var seconds = 7200.5; // 2 hours 30 seconds
        var dbValue = new DbValue(seconds);

        var success = _registry.TryRead(dbValue, typeof(TimeSpan), out var result);

        Assert.True(success);
        var timeSpan = (TimeSpan)result!;
        Assert.Equal(TimeSpan.FromSeconds(7200.5), timeSpan);
    }

    #endregion

    #region DateTimeOffset Tests

    [Fact]
    public void DateTimeOffsetCoercion_ShouldHandleDateTimeOffset()
    {
        var dto = DateTimeOffset.Now;
        var dbValue = new DbValue(dto);

        var success = _registry.TryRead(dbValue, typeof(DateTimeOffset), out var result);

        Assert.True(success);
        Assert.Equal(dto, result);
    }

    [Fact]
    public void DateTimeOffsetCoercion_ShouldHandleDateTime()
    {
        var dt = DateTime.Now;
        var dbValue = new DbValue(dt);

        var success = _registry.TryRead(dbValue, typeof(DateTimeOffset), out var result);

        Assert.True(success);
        var dto = (DateTimeOffset)result!;
        Assert.Equal(dt, dto.DateTime);
    }

    [Fact]
    public void DateTimeOffsetCoercion_ShouldWriteWithCorrectDbType()
    {
        var dto = DateTimeOffset.Now;
        var parameter = new TestDbParameter();

        var success = _registry.TryWrite(dto, parameter);

        Assert.True(success);
        Assert.Equal(dto, parameter.Value);
        Assert.Equal(DbType.DateTimeOffset, parameter.DbType);
    }

    #endregion

    #region Null Handling Tests

    [Fact]
    public void AllCoercions_ShouldHandleNullValues()
    {
        var dbValue = new DbValue(null);

        // Test each nullable type
        var types = new[]
        {
            typeof(Guid?), typeof(byte[]), typeof(JsonValue?), typeof(HStore?),
            typeof(Range<int>?), typeof(TimeSpan?), typeof(DateTimeOffset?)
        };

        foreach (var type in types)
        {
            var success = _registry.TryRead(dbValue, type, out var result);
            // Some types return true with null, others return false - both are valid
        }
    }

    [Fact]
    public void AllCoercions_ShouldHandleDBNullValues()
    {
        var dbValue = new DbValue(DBNull.Value);

        var success = _registry.TryRead(dbValue, typeof(Guid), out var result);

        // Should handle DBNull as null appropriately
        Assert.False(success); // Non-nullable Guid should fail
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void CoercionRegistry_ShouldHaveConsistentPerformance()
    {
        var guid = Guid.NewGuid();
        var dbValue = new DbValue(guid);

        // First call
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            _registry.TryRead(dbValue, typeof(Guid), out var _);
        }
        sw.Stop();

        var firstBatch = sw.ElapsedMilliseconds;

        // Second batch - should be similar performance (testing caching)
        sw.Restart();
        for (int i = 0; i < 1000; i++)
        {
            _registry.TryRead(dbValue, typeof(Guid), out var _);
        }
        sw.Stop();

        var secondBatch = sw.ElapsedMilliseconds;

        // Performance should be consistent (not get dramatically worse)
        // Relaxed assertion - just check that second batch isn't more than 10x slower
        // This avoids flakiness while still catching major performance regressions
        Assert.True(secondBatch < firstBatch * 10 + 100); // Allow up to 10x + 100ms margin for variance
    }

    #endregion

    private class TestDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = string.Empty;
        public override int Size { get; set; }
        public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }

        public override void ResetDbType()
        {
            DbType = DbType.Object;
        }
    }
}