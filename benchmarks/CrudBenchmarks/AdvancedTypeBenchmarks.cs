using System;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using pengdows.crud.enums;
using pengdows.crud.types;
using pengdows.crud.types.valueobjects;

namespace CrudBenchmarks;

/// <summary>
/// Benchmarks for advanced type handling performance.
/// Ensures pengdows.crud maintains or exceeds Dapper performance for type coercion.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
[MarkdownExporter]
public class AdvancedTypeBenchmarks
{
    private AdvancedTypeRegistry? _registry;
    private TestDbParameter? _parameter;
    private Inet _inetValue;
    private Range<int> _rangeValue;
    private Geometry? _geometryValue;
    private byte[]? _rowVersionBytes;
    private SupportedDatabase _geometryProvider;

    [GlobalSetup]
    public void Setup()
    {
        _registry = AdvancedTypeRegistry.Shared;
        _parameter = new TestDbParameter();
        _inetValue = new Inet(IPAddress.Parse("192.168.1.1"), 24);
        _rangeValue = new Range<int>(1, 100, true, false);
        _geometryValue = Geometry.FromWellKnownText("POINT(1 2)", 4326);
        _rowVersionBytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };
        _geometryProvider = Type.GetType("Microsoft.SqlServer.Types.SqlGeometry, Microsoft.SqlServer.Types") != null
            ? SupportedDatabase.SqlServer
            : SupportedDatabase.PostgreSql;
    }

    [Benchmark(Baseline = true)]
    public bool ConfigureSimpleParameter()
    {
        // Baseline: simple parameter configuration without advanced types
        _parameter!.Value = "test string";
        _parameter.DbType = DbType.String;
        return true;
    }

    [Benchmark]
    public bool ConfigureInetParameter()
    {
        return _registry!.TryConfigureParameter(_parameter!, typeof(Inet), _inetValue, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool ConfigureRangeParameter()
    {
        return _registry!.TryConfigureParameter(_parameter!, typeof(Range<int>), _rangeValue,
            SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool ConfigureGeometryParameter()
    {
        return _registry!.TryConfigureParameter(_parameter!, typeof(Geometry), _geometryValue!, _geometryProvider);
    }

    [Benchmark]
    public bool ConfigureRowVersionParameter()
    {
        var rowVersion = RowVersion.FromBytes(_rowVersionBytes!);
        return _registry!.TryConfigureParameter(_parameter!, typeof(RowVersion), rowVersion,
            SupportedDatabase.SqlServer);
    }

    [Benchmark]
    public bool ConfigureNullParameter()
    {
        return _registry!.TryConfigureParameter(_parameter!, typeof(Inet), null, SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public bool ConfigureUnregisteredType()
    {
        // This should fail fast and not cause performance issues
        return _registry!.TryConfigureParameter(_parameter!, typeof(Uri), new Uri("https://example.com"),
            SupportedDatabase.SqlServer);
    }

    [Benchmark]
    public bool ConfigureCachedParameter()
    {
        // Second call should be faster due to caching
        var success1 =
            _registry!.TryConfigureParameter(_parameter!, typeof(Inet), _inetValue, SupportedDatabase.PostgreSql);
        var success2 =
            _registry!.TryConfigureParameter(_parameter!, typeof(Inet), _inetValue, SupportedDatabase.PostgreSql);
        return success1 && success2;
    }

    /// <summary>
    /// Test converter performance
    /// </summary>
    [Benchmark]
    public object? ConvertInetFromString()
    {
        var converter = _registry!.GetConverter(typeof(Inet));
        return converter?.FromProviderValue("192.168.1.1/24", SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public object? ConvertRangeFromString()
    {
        var converter = _registry!.GetConverter(typeof(Range<int>));
        return converter?.FromProviderValue("[1,100)", SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public object? ConvertGeometryFromWKT()
    {
        var converter = _registry!.GetConverter(typeof(Geometry));
        return converter?.FromProviderValue("POINT(1 2)", _geometryProvider);
    }

    /// <summary>
    /// Test mapping lookup performance
    /// </summary>
    [Benchmark]
    public object? GetMapping()
    {
        return _registry!.GetMapping(typeof(Inet), SupportedDatabase.PostgreSql);
    }

    [Benchmark]
    public object? GetConverter()
    {
        return _registry!.GetConverter(typeof(Inet));
    }

    /// <summary>
    /// Simulate the actual hot path through SqlDialect parameter configuration
    /// </summary>
    [Benchmark]
    public bool HotPathParameterSetup()
    {
        // This simulates the exact call pattern from SqlDialect.AddParameterWithValue
        var runtimeType = _inetValue.GetType();
        return _registry!.TryConfigureParameter(_parameter!, runtimeType, _inetValue, SupportedDatabase.PostgreSql);
    }

    /// <summary>
    /// Simple test parameter for benchmarking
    /// </summary>
    private class TestDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
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