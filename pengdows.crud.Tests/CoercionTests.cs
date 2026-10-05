using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using pengdows.crud.fakeDb;
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

    #endregion

    #region ByteArray Tests

    [Fact]
    public void ByteArrayCoercion_ShouldHandleByteArray()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        var dbValue = new DbValue(bytes);

        var success = _registry.TryRead(dbValue, typeof(byte[]), out var result);

        Assert.True(success);
        Assert.Equal(bytes, result);
    }

    [Fact]
    public void ByteArrayCoercion_ShouldHandleReadOnlyMemory()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03 };
        var memory = new ReadOnlyMemory<byte>(bytes);
        var dbValue = new DbValue(memory);

        var success = _registry.TryRead(dbValue, typeof(byte[]), out var result);

        Assert.True(success);
        Assert.Equal(bytes, result);
    }

    [Fact]
    public void ByteArrayCoercion_ShouldHandleArraySegment()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var segment = new ArraySegment<byte>(bytes, 1, 3);
        var dbValue = new DbValue(segment);

        var success = _registry.TryRead(dbValue, typeof(byte[]), out var result);

        Assert.True(success);
        var resultBytes = (byte[])result!;
        Assert.Equal(3, resultBytes.Length);
        Assert.Equal(new byte[] { 0x02, 0x03, 0x04 }, resultBytes);
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

        // REV-062: this asserted nothing. A read may succeed or decline, but never invents a value.
        foreach (var type in types)
        {
            var success = _registry.TryRead(dbValue, type, out var result);
            Assert.True(result == null, $"{type.Name}: read null as {result} (success={success})");
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
        for (var i = 0; i < 1000; i++)
        {
            _registry.TryRead(dbValue, typeof(Guid), out _);
        }

        sw.Stop();

        var firstBatch = sw.ElapsedMilliseconds;

        // Second batch - should be similar performance (testing caching)
        sw.Restart();
        for (var i = 0; i < 1000; i++)
        {
            _registry.TryRead(dbValue, typeof(Guid), out _);
        }

        sw.Stop();

        var secondBatch = sw.ElapsedMilliseconds;

        // Performance should be consistent (not get dramatically worse)
        // Relaxed assertion - just check that second batch isn't more than 10x slower
        // This avoids flakiness while still catching major performance regressions
        Assert.True(secondBatch < firstBatch * 10 + 100); // Allow up to 10x + 100ms margin for variance
    }

    #endregion
}