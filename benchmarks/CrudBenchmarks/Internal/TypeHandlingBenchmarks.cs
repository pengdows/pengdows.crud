using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using BenchmarkDotNet.Attributes;
using pengdows.crud.enums;
using pengdows.crud.types;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;

namespace CrudBenchmarks.Internal;

/// <summary>
/// Comprehensive benchmarks for type handling performance in pengdows.crud.
/// Covers both AdvancedTypeRegistry (parameter configuration) and CoercionRegistry (type conversion).
/// Ensures pengdows.crud maintains or exceeds Dapper performance for type coercion and parameter setup.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[MarkdownExporter]
public class TypeHandlingBenchmarks
{
    private AdvancedTypeRegistry? _advancedRegistry;
    private CoercionRegistry? _coercionRegistry;
    private TestDbParameter? _parameter;

    // Test data
    private Guid _guid;
    private Inet _inetValue;
    private Range<int> _rangeValue;
    private Geometry? _geometryValue;
    private byte[]? _rowVersionBytes;
    private JsonValue _jsonValue;
    private HStore _hstoreValue;
    private TimeSpan _timeSpan;
    private DateTimeOffset _dateTimeOffset;
    private int[]? _intArray;
    private string[]? _stringArray;
    private SupportedDatabase _geometryProvider;

    [GlobalSetup]
    public void Setup()
    {
        _advancedRegistry = AdvancedTypeRegistry.Shared;
        _coercionRegistry = new CoercionRegistry();
        _parameter = new TestDbParameter();

        // Initialize test data
        _guid = Guid.NewGuid();
        _inetValue = new Inet(IPAddress.Parse("192.168.1.1"), 24);
        _rangeValue = new Range<int>(1, 100, true, false);
        _geometryValue = Geometry.FromWellKnownText("POINT(1 2)", 4326);
        _rowVersionBytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };
        _jsonValue = new JsonValue("{\"name\":\"test\",\"value\":123,\"active\":true}");
        _hstoreValue = HStore.Parse("\"key1\"=>\"value1\", \"key2\"=>NULL, \"key3\"=>\"value with spaces\"");
        _timeSpan = TimeSpan.FromHours(2.5);
        _dateTimeOffset = DateTimeOffset.Now;
        _intArray = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        _stringArray = new[] { "hello", "world", "test", "coercion", "performance" };

        _geometryProvider = Type.GetType("Microsoft.SqlServer.Types.SqlGeometry, Microsoft.SqlServer.Types") != null
            ? SupportedDatabase.SqlServer
            : SupportedDatabase.PostgreSql;
    }

    // ============================================================================
    // BASELINE: Simple parameter configuration without advanced types
    // ============================================================================

    [Benchmark(Baseline = true)]
    public bool Baseline_SimpleParameter()
    {
        _parameter!.Value = "test string";
        _parameter.DbType = DbType.String;
        return true;
    }

    // ============================================================================
    // ADVANCED TYPE REGISTRY: Parameter Configuration
    // ============================================================================

    [Benchmark]
    public bool AdvancedType_Inet_Configure()
    {
        return _advancedRegistry!.TryConfigureParameter(_parameter!, typeof(Inet), _inetValue, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool AdvancedType_Range_Configure()
    {
        return _advancedRegistry!.TryConfigureParameter(_parameter!, typeof(Range<int>), _rangeValue, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool AdvancedType_Geometry_Configure()
    {
        return _advancedRegistry!.TryConfigureParameter(_parameter!, typeof(Geometry), _geometryValue!, _geometryProvider);
    }

    [Benchmark]
    public bool AdvancedType_RowVersion_Configure()
    {
        var rowVersion = RowVersion.FromBytes(_rowVersionBytes!);
        return _advancedRegistry!.TryConfigureParameter(_parameter!, typeof(RowVersion), rowVersion, SupportedDatabase.SqlServer);
    }

    [Benchmark]
    public bool AdvancedType_Null_Configure()
    {
        return _advancedRegistry!.TryConfigureParameter(_parameter!, typeof(Inet), null, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool AdvancedType_Cached_Configure()
    {
        // Second call should be faster due to caching
        var success1 = _advancedRegistry!.TryConfigureParameter(_parameter!, typeof(Inet), _inetValue, SupportedDatabase.PostgreSql);
        var success2 = _advancedRegistry!.TryConfigureParameter(_parameter!, typeof(Inet), _inetValue, SupportedDatabase.PostgreSql);
        return success1 && success2;
    }

    // ============================================================================
    // COERCION REGISTRY: Type Conversion (Write Operations)
    // ============================================================================

    [Benchmark]
    public bool Coercion_Guid_Write()
    {
        return _coercionRegistry!.TryWrite(_guid, _parameter!, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool Coercion_RowVersion_Write()
    {
        return _coercionRegistry!.TryWrite(_rowVersionBytes!, _parameter!, SupportedDatabase.SqlServer);
    }

    [Benchmark]
    public bool Coercion_Json_Write()
    {
        return _coercionRegistry!.TryWrite(_jsonValue, _parameter!, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool Coercion_HStore_Write()
    {
        return _coercionRegistry!.TryWrite(_hstoreValue, _parameter!, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool Coercion_Range_Write()
    {
        return _coercionRegistry!.TryWrite(_rangeValue, _parameter!, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool Coercion_TimeSpan_Write()
    {
        return _coercionRegistry!.TryWrite(_timeSpan, _parameter!);
    }

    [Benchmark]
    public bool Coercion_DateTimeOffset_Write()
    {
        return _coercionRegistry!.TryWrite(_dateTimeOffset, _parameter!);
    }

    [Benchmark]
    public bool Coercion_IntArray_Write()
    {
        return _coercionRegistry!.TryWrite(_intArray!, _parameter!, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool Coercion_StringArray_Write()
    {
        return _coercionRegistry!.TryWrite(_stringArray!, _parameter!, SupportedDatabase.PostgreSql);
    }

    // ============================================================================
    // COERCION REGISTRY: Type Conversion (Read Operations)
    // ============================================================================

    [Benchmark]
    public bool Coercion_Guid_Read()
    {
        var dbValue = new DbValue(_guid);
        return _coercionRegistry!.TryRead(dbValue, typeof(Guid), out _);
    }

    [Benchmark]
    public bool Coercion_RowVersion_Read()
    {
        var dbValue = new DbValue(_rowVersionBytes!);
        return _coercionRegistry!.TryRead(dbValue, typeof(byte[]), out _);
    }

    [Benchmark]
    public bool Coercion_Json_Read()
    {
        var jsonText = _jsonValue.AsString();
        var dbValue = new DbValue(jsonText);
        return _coercionRegistry!.TryRead(dbValue, typeof(JsonValue), out _);
    }

    [Benchmark]
    public bool Coercion_HStore_Read()
    {
        var hstoreText = _hstoreValue.ToString();
        var dbValue = new DbValue(hstoreText);
        return _coercionRegistry!.TryRead(dbValue, typeof(HStore), out _);
    }

    [Benchmark]
    public bool Coercion_Range_Read()
    {
        var rangeText = _rangeValue.ToString();
        var dbValue = new DbValue(rangeText);
        return _coercionRegistry!.TryRead(dbValue, typeof(Range<int>), out _);
    }

    [Benchmark]
    public bool Coercion_TimeSpan_Read()
    {
        var dbValue = new DbValue(_timeSpan);
        return _coercionRegistry!.TryRead(dbValue, typeof(TimeSpan), out _);
    }

    [Benchmark]
    public bool Coercion_DateTimeOffset_Read()
    {
        var dbValue = new DbValue(_dateTimeOffset);
        return _coercionRegistry!.TryRead(dbValue, typeof(DateTimeOffset), out _);
    }

    [Benchmark]
    public bool Coercion_IntArray_Read()
    {
        var dbValue = new DbValue(_intArray!);
        return _coercionRegistry!.TryRead(dbValue, typeof(int[]), out _);
    }

    [Benchmark]
    public bool Coercion_StringArray_Read()
    {
        var dbValue = new DbValue(_stringArray!);
        return _coercionRegistry!.TryRead(dbValue, typeof(string[]), out _);
    }

    // ============================================================================
    // COMPLEX SCENARIOS: Parsing from String
    // ============================================================================

    [Benchmark]
    public object? Complex_JsonParsing()
    {
        var jsonText = "{\"name\":\"test\",\"value\":123,\"nested\":{\"array\":[1,2,3]}}";
        var dbValue = new DbValue(jsonText);
        return _coercionRegistry!.TryRead(dbValue, typeof(JsonValue), out _);
    }

    [Benchmark]
    public object? Complex_HStoreParsing()
    {
        var hstoreText = "\"key1\"=>\"simple\", \"key2\"=>\"value with, comma\", \"key3\"=>\"value with \\\"quotes\\\"\", \"key4\"=>NULL";
        var dbValue = new DbValue(hstoreText);
        return _coercionRegistry!.TryRead(dbValue, typeof(HStore), out _);
    }

    [Benchmark]
    public object? Complex_RangeParsing()
    {
        var rangeText = "[2023-01-01 10:30:00,2023-12-31 23:59:59)";
        var dbValue = new DbValue(rangeText);
        return _coercionRegistry!.TryRead(dbValue, typeof(Range<DateTime>), out _);
    }

    // ============================================================================
    // PROVIDER-SPECIFIC OPTIMIZATIONS
    // ============================================================================

    [Benchmark]
    public bool ProviderSpecific_PostgreSqlGuid()
    {
        var guidBytes = _guid.ToByteArray();
        var dbValue = new DbValue(guidBytes);
        return _coercionRegistry!.TryRead(dbValue, typeof(Guid), out _, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool ProviderSpecific_SqlServerJson()
    {
        return _coercionRegistry!.TryWrite(_jsonValue, _parameter!, SupportedDatabase.SqlServer);
    }

    [Benchmark]
    public bool ProviderSpecific_MySqlBoolean()
    {
        var dbValue = new DbValue((byte)1); // MySQL BIT(1)
        return _coercionRegistry!.TryRead(dbValue, typeof(bool), out _, SupportedDatabase.MySql);
    }

    // ============================================================================
    // HOT PATH SIMULATION: Mixed Workload
    // ============================================================================

    [Benchmark]
    public bool HotPath_MixedCoercion()
    {
        var success = true;
        success &= _coercionRegistry!.TryWrite(_guid, _parameter!);
        success &= _coercionRegistry!.TryWrite(_jsonValue, _parameter!);
        success &= _coercionRegistry!.TryWrite(_rangeValue, _parameter!);
        success &= _coercionRegistry!.TryWrite(_timeSpan, _parameter!);
        return success;
    }

    [Benchmark]
    public bool HotPath_CachedLookup()
    {
        // Test repeated lookups of the same type (should be cached)
        var success = true;
        var dbValue = new DbValue(_guid);
        for (var i = 0; i < 10; i++)
        {
            success &= _coercionRegistry!.TryRead(dbValue, typeof(Guid), out _);
        }
        return success;
    }

    [Benchmark]
    public bool HotPath_AdvancedTypeParameter()
    {
        // Simulate the actual call pattern from SqlDialect.AddParameterWithValue
        var runtimeType = _inetValue.GetType();
        return _advancedRegistry!.TryConfigureParameter(_parameter!, runtimeType, _inetValue, SupportedDatabase.PostgreSql);
    }

    // ============================================================================
    // REGISTRY LOOKUP PERFORMANCE
    // ============================================================================

    [Benchmark]
    public object? Lookup_GetMapping()
    {
        return _advancedRegistry!.GetMapping(typeof(Inet), SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public object? Lookup_GetConverter()
    {
        return _advancedRegistry!.GetConverter(typeof(Inet));
    }

    [Benchmark]
    public object? Coercion_InetConverter()
    {
        var converter = _advancedRegistry!.GetConverter(typeof(Inet));
        return converter?.FromProviderValue("192.168.1.1/24", SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public object? Coercion_RangeConverter()
    {
        var converter = _advancedRegistry!.GetConverter(typeof(Range<int>));
        return converter?.FromProviderValue("[1,100)", SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public object? Coercion_GeometryConverter()
    {
        var converter = _advancedRegistry!.GetConverter(typeof(Geometry));
        return converter?.FromProviderValue("POINT(1 2)", _geometryProvider);
    }

    // ============================================================================
    // FAILURE CASES: Unregistered Types
    // ============================================================================

    [Benchmark]
    public bool Failure_UnregisteredType()
    {
        // This should fail fast and not cause performance issues
        return _advancedRegistry!.TryConfigureParameter(_parameter!, typeof(Uri), new Uri("https://example.com"), SupportedDatabase.SqlServer);
    }

    // ============================================================================
    // TEST DB PARAMETER
    // ============================================================================

    private class TestDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override System.Data.ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull] public override string ParameterName { get; set; } = string.Empty;
        public override int Size { get; set; }
        [AllowNull] public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }

        public override void ResetDbType()
        {
            DbType = DbType.Object;
        }
    }
}
