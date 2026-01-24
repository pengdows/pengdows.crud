using System;
using System.Data.Common;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;

namespace CrudBenchmarks;

/// <summary>
/// Benchmarks for weird database type coercion performance.
/// Validates that coercions are fast and allocation-efficient.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
[MarkdownExporter]
public class WeirdTypeCoercionBenchmarks
{
    private CoercionRegistry? _registry;
    private TestDbParameter? _parameter;

    // Test data
    private Guid _guid;
    private byte[]? _rowVersionBytes;
    private JsonValue _jsonValue;
    private HStore _hstoreValue;
    private Range<int> _intRange;
    private TimeSpan _timeSpan;
    private DateTimeOffset _dateTimeOffset;
    private int[]? _intArray;
    private string[]? _stringArray;

    [GlobalSetup]
    public void Setup()
    {
        _registry = new CoercionRegistry();
        _parameter = new TestDbParameter();

        _guid = Guid.NewGuid();
        _rowVersionBytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };
        _jsonValue = new JsonValue("{\"name\":\"test\",\"value\":123,\"active\":true}");
        _hstoreValue = HStore.Parse("\"key1\"=>\"value1\", \"key2\"=>NULL, \"key3\"=>\"value with spaces\"");
        _intRange = new Range<int>(1, 100, true, false);
        _timeSpan = TimeSpan.FromHours(2.5);
        _dateTimeOffset = DateTimeOffset.Now;
        _intArray = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        _stringArray = new[] { "hello", "world", "test", "coercion", "performance" };
    }

    [Benchmark(Baseline = true)]
    public bool BaselineParameterSetup()
    {
        // Baseline: simple parameter setup without coercion
        _parameter!.Value = "test string";
        _parameter.DbType = System.Data.DbType.String;
        return true;
    }

    [Benchmark]
    public bool GuidCoercion_Write()
    {
        return _registry!.TryWrite(_guid, _parameter!, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool GuidCoercion_Read()
    {
        var dbValue = new DbValue(_guid);
        return _registry!.TryRead(dbValue, typeof(Guid), out var _);
    }

    [Benchmark]
    public bool RowVersionCoercion_Write()
    {
        return _registry!.TryWrite(_rowVersionBytes!, _parameter!, SupportedDatabase.SqlServer);
    }

    [Benchmark]
    public bool RowVersionCoercion_Read()
    {
        var dbValue = new DbValue(_rowVersionBytes!);
        return _registry!.TryRead(dbValue, typeof(byte[]), out var _);
    }

    [Benchmark]
    public bool JsonCoercion_Write()
    {
        return _registry!.TryWrite(_jsonValue, _parameter!, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool JsonCoercion_Read()
    {
        var jsonText = _jsonValue.AsString();
        var dbValue = new DbValue(jsonText);
        return _registry!.TryRead(dbValue, typeof(JsonValue), out var _);
    }

    [Benchmark]
    public bool HStoreCoercion_Write()
    {
        return _registry!.TryWrite(_hstoreValue, _parameter!, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool HStoreCoercion_Read()
    {
        var hstoreText = _hstoreValue.ToString();
        var dbValue = new DbValue(hstoreText);
        return _registry!.TryRead(dbValue, typeof(HStore), out var _);
    }

    [Benchmark]
    public bool RangeCoercion_Write()
    {
        return _registry!.TryWrite(_intRange, _parameter!, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool RangeCoercion_Read()
    {
        var rangeText = _intRange.ToString();
        var dbValue = new DbValue(rangeText);
        return _registry!.TryRead(dbValue, typeof(Range<int>), out var _);
    }

    [Benchmark]
    public bool TimeSpanCoercion_Write()
    {
        return _registry!.TryWrite(_timeSpan, _parameter!);
    }

    [Benchmark]
    public bool TimeSpanCoercion_Read()
    {
        var dbValue = new DbValue(_timeSpan);
        return _registry!.TryRead(dbValue, typeof(TimeSpan), out var _);
    }

    [Benchmark]
    public bool DateTimeOffsetCoercion_Write()
    {
        return _registry!.TryWrite(_dateTimeOffset, _parameter!);
    }

    [Benchmark]
    public bool DateTimeOffsetCoercion_Read()
    {
        var dbValue = new DbValue(_dateTimeOffset);
        return _registry!.TryRead(dbValue, typeof(DateTimeOffset), out var _);
    }

    [Benchmark]
    public bool IntArrayCoercion_Write()
    {
        return _registry!.TryWrite(_intArray!, _parameter!, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool IntArrayCoercion_Read()
    {
        var dbValue = new DbValue(_intArray!);
        return _registry!.TryRead(dbValue, typeof(int[]), out var _);
    }

    [Benchmark]
    public bool StringArrayCoercion_Write()
    {
        return _registry!.TryWrite(_stringArray!, _parameter!, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool StringArrayCoercion_Read()
    {
        var dbValue = new DbValue(_stringArray!);
        return _registry!.TryRead(dbValue, typeof(string[]), out var _);
    }

    // Complex scenarios
    [Benchmark]
    public bool JsonParsingFromString()
    {
        var jsonText = "{\"name\":\"test\",\"value\":123,\"nested\":{\"array\":[1,2,3]}}";
        var dbValue = new DbValue(jsonText);
        return _registry!.TryRead(dbValue, typeof(JsonValue), out var _);
    }

    [Benchmark]
    public bool HStoreParsingFromComplexString()
    {
        var hstoreText = "\"key1\"=>\"simple\", \"key2\"=>\"value with, comma\", \"key3\"=>\"value with \\\"quotes\\\"\", \"key4\"=>NULL";
        var dbValue = new DbValue(hstoreText);
        return _registry!.TryRead(dbValue, typeof(HStore), out var _);
    }

    [Benchmark]
    public bool RangeParsingFromString()
    {
        var rangeText = "[2023-01-01 10:30:00,2023-12-31 23:59:59)";
        var dbValue = new DbValue(rangeText);
        return _registry!.TryRead(dbValue, typeof(Range<DateTime>), out var _);
    }

    // Provider-specific optimizations
    [Benchmark]
    public bool PostgreSqlGuidOptimization()
    {
        var guidBytes = _guid.ToByteArray();
        var dbValue = new DbValue(guidBytes);
        return _registry!.TryRead(dbValue, typeof(Guid), out var _, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool SqlServerJsonHandling()
    {
        return _registry!.TryWrite(_jsonValue, _parameter!, SupportedDatabase.SqlServer);
    }

    [Benchmark]
    public bool MySqlBooleanHandling()
    {
        var dbValue = new DbValue((byte)1); // MySQL BIT(1)
        return _registry!.TryRead(dbValue, typeof(bool), out var _, SupportedDatabase.MySql);
    }

    // Hot path simulation
    [Benchmark]
    public bool HotPathMixedCoercion()
    {
        // Simulate typical mixed workload
        var success = true;

        success &= _registry!.TryWrite(_guid, _parameter!);
        success &= _registry!.TryWrite(_jsonValue, _parameter!);
        success &= _registry!.TryWrite(_intRange, _parameter!);
        success &= _registry!.TryWrite(_timeSpan, _parameter!);

        return success;
    }

    [Benchmark]
    public bool CachedLookupPerformance()
    {
        // Test repeated lookups of the same type (should be cached)
        var success = true;
        var dbValue = new DbValue(_guid);

        for (int i = 0; i < 10; i++)
        {
            success &= _registry!.TryRead(dbValue, typeof(Guid), out var _);
        }

        return success;
    }

    private class TestDbParameter : DbParameter
    {
        public override System.Data.DbType DbType { get; set; }
        public override System.Data.ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        public override int Size { get; set; }
        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }

        public override void ResetDbType()
        {
            DbType = System.Data.DbType.Object;
        }
    }
}
