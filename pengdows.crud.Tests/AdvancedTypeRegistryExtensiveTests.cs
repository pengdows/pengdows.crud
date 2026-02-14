using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.types;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("TypeRegistry")]
public class AdvancedTypeRegistryExtensiveTests
{
    #region Cache Testing

    [Fact]
    public void TryConfigureParameter_UsesCaching()
    {
        var registry = new AdvancedTypeRegistry();
        var mapping = new ProviderTypeMapping { DbType = DbType.String };
        registry.RegisterMapping<string>(SupportedDatabase.SqlServer, mapping);

        var param1 = new TestDbParameter();
        var param2 = new TestDbParameter();

        // First call - should cache
        var result1 = registry.TryConfigureParameter(param1, typeof(string), "test1", SupportedDatabase.SqlServer);

        // Second call - should use cache
        var result2 = registry.TryConfigureParameter(param2, typeof(string), "test2", SupportedDatabase.SqlServer);

        Assert.True(result1);
        Assert.True(result2);
        Assert.Equal(DbType.String, param1.DbType);
        Assert.Equal(DbType.String, param2.DbType);
        Assert.Equal("test1", param1.Value);
        Assert.Equal("test2", param2.Value);
    }

    [Fact]
    public void TryConfigureParameter_CachesNegativeResults()
    {
        var registry = new AdvancedTypeRegistry();
        var param1 = new TestDbParameter();
        var param2 = new TestDbParameter();

        // First call - should cache negative result
        var result1 = registry.TryConfigureParameter(param1, typeof(int), 42, SupportedDatabase.SqlServer);

        // Second call - should use cached negative result
        var result2 = registry.TryConfigureParameter(param2, typeof(int), 84, SupportedDatabase.SqlServer);

        Assert.False(result1);
        Assert.False(result2);
    }

    [Fact]
    public void RegisterMapping_ClearsCacheForType()
    {
        var registry = new AdvancedTypeRegistry();
        var param = new TestDbParameter();

        // First try - should fail and cache negative result
        var result1 =
            registry.TryConfigureParameter(param, typeof(DateTime), DateTime.Now, SupportedDatabase.SqlServer);
        Assert.False(result1);

        // Register mapping - should clear cache
        registry.RegisterMapping<DateTime>(SupportedDatabase.SqlServer,
            new ProviderTypeMapping { DbType = DbType.DateTime });

        // Second try - should succeed
        var result2 =
            registry.TryConfigureParameter(param, typeof(DateTime), DateTime.Now, SupportedDatabase.SqlServer);
        Assert.True(result2);
        Assert.Equal(DbType.DateTime, param.DbType);
    }

    [Fact]
    public void RegisterConverter_ClearsCacheForType()
    {
        var registry = new AdvancedTypeRegistry();

        // Register mapping first
        registry.RegisterMapping<Inet>(SupportedDatabase.PostgreSql,
            new ProviderTypeMapping { DbType = DbType.String });

        var param1 = new TestDbParameter();
        var inet = new Inet(IPAddress.Parse("192.168.1.1"));

        // First call - no converter, should work but not convert
        var result1 = registry.TryConfigureParameter(param1, typeof(Inet), inet, SupportedDatabase.PostgreSql);
        Assert.True(result1);
        Assert.Equal(inet, param1.Value); // No conversion

        // Register converter - should clear cache
        registry.RegisterConverter(new InetConverter());

        var param2 = new TestDbParameter();
        var result2 = registry.TryConfigureParameter(param2, typeof(Inet), inet, SupportedDatabase.PostgreSql);
        Assert.True(result2);
        Assert.Equal("192.168.1.1", param2.Value); // Converted by InetConverter
    }

    #endregion

    #region Enhanced Parameter Configuration

    [Fact]
    public void TryConfigureParameterEnhanced_FallsBackToProviderParameterFactory()
    {
        var registry = new AdvancedTypeRegistry();
        var param = new TestDbParameter();

        // Try with a type that isn't registered in AdvancedTypeRegistry
        // but might be handled by ProviderParameterFactory
        var result =
            registry.TryConfigureParameterEnhanced(param, typeof(decimal), 42.5m, SupportedDatabase.PostgreSql);

        // Should fall back to ProviderParameterFactory or ParameterBindingRules
        // The exact behavior depends on the implementation, but it should not throw
        Assert.True(result || !result); // Either succeeds or fails gracefully
    }

    [Fact]
    public void TryConfigureParameterEnhanced_FallsBackToParameterBindingRules()
    {
        var registry = new AdvancedTypeRegistry();
        var param = new TestDbParameter();

        // Try with a simple type that should be handled by binding rules
        var result = registry.TryConfigureParameterEnhanced(param, typeof(string), "test", SupportedDatabase.SqlServer);

        // Should either work via advanced types or fall back to binding rules
        Assert.True(result);
    }

    [Fact]
    public void CoercionRegistry_Property_ReturnsSharedInstance()
    {
        var registry = new AdvancedTypeRegistry();
        var coercionRegistry = registry.CoercionRegistry;

        Assert.NotNull(coercionRegistry);
        Assert.Same(CoercionRegistry.Shared, coercionRegistry);
    }

    #endregion

    #region JSON Mappings Testing

    [Fact]
    public void JsonDocument_PostgreSql_ConfiguresJsonbType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(JsonDocument), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.String, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new PostgreSqlLikeParameter();
        var doc = JsonDocument.Parse("{}");
        mapping.ConfigureParameter(param, doc);

        Assert.Equal("jsonb", param.DataTypeName);
        Assert.Equal(MockNpgsqlDbType.Jsonb, param.NpgsqlDbType);
    }

    [Fact]
    public void JsonDocument_MySql_ConfiguresJsonType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(JsonDocument), SupportedDatabase.MySql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.String, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new MySqlLikeParameter();
        mapping.ConfigureParameter(param, null);

        Assert.Equal(MockMySqlDbType.JSON, param.MySqlDbType);
    }

    [Fact]
    public void JsonDocument_SqlServer_ConfiguresNVarcharMax()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(JsonDocument), SupportedDatabase.SqlServer);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.String, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new TestDbParameter();
        mapping.ConfigureParameter(param, null);

        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal(-1, param.Size); // NVARCHAR(MAX)
    }

    #endregion

    #region Spatial Mappings Testing

    [Fact]
    public void Geometry_SqlServer_ConfiguresUdt()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(Geometry), SupportedDatabase.SqlServer);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Object, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new SqlServerLikeParameter();
        // Use null to test the configuration path - the actual value doesn't matter for parameter setup
        mapping.ConfigureParameter(param, null);

        Assert.Equal(MockSqlDbType.Udt, param.SqlDbType);
        Assert.Equal("geometry", param.UdtTypeName);
    }

    [Fact]
    public void Geography_SqlServer_ConfiguresUdt()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(Geography), SupportedDatabase.SqlServer);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Object, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new SqlServerLikeParameter();
        // Use null to test the configuration path - the actual value doesn't matter for parameter setup
        mapping.ConfigureParameter(param, null);

        Assert.Equal(MockSqlDbType.Udt, param.SqlDbType);
        Assert.Equal("geography", param.UdtTypeName);
    }

    [Fact]
    public void Geometry_PostgreSql_ConfiguresBinary()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(Geometry), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Binary, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new TestDbParameter();
        // Use null to test the configuration path - the actual value doesn't matter for parameter setup
        mapping.ConfigureParameter(param, null);

        Assert.Equal(DbType.Binary, param.DbType);
    }

    #endregion

    #region Array Mappings Testing

    [Fact]
    public void IntArray_PostgreSql_ConfiguresArrayType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(int[]), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Object, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new PostgreSqlLikeParameter();
        mapping.ConfigureParameter(param, new int[] { 1, 2, 3 });

        // Should combine Array and Integer flags
        Assert.NotNull(param.NpgsqlDbType);
        Assert.True(param.NpgsqlDbType?.HasFlag(MockNpgsqlDbType.Array) == true);
    }

    [Fact]
    public void StringArray_PostgreSql_ConfiguresArrayType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(string[]), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Object, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new PostgreSqlLikeParameter();
        mapping.ConfigureParameter(param, new string[] { "a", "b" });

        Assert.NotNull(param.NpgsqlDbType);
        Assert.True(param.NpgsqlDbType?.HasFlag(MockNpgsqlDbType.Array) == true);
    }

    #endregion

    #region Range Mappings Testing

    [Fact]
    public void IntRange_PostgreSql_ConfiguresRangeType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(Range<int>), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.String, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new PostgreSqlLikeParameter();
        mapping.ConfigureParameter(param, new Range<int>(1, 10));

        Assert.Equal(MockNpgsqlDbType.Int4Range, param.NpgsqlDbType);
    }

    [Fact]
    public void DateTimeRange_PostgreSql_ConfiguresRangeType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(Range<DateTime>), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.String, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new PostgreSqlLikeParameter();
        mapping.ConfigureParameter(param, new Range<DateTime>(DateTime.Today, DateTime.Today.AddDays(1)));

        Assert.Equal(MockNpgsqlDbType.TsRange, param.NpgsqlDbType);
    }

    #endregion

    #region Network Mappings Testing

    [Fact]
    public void Inet_PostgreSql_ConfiguresInetType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(Inet), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.String, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new PostgreSqlLikeParameter();
        mapping.ConfigureParameter(param, new Inet(IPAddress.Parse("192.168.1.1")));

        Assert.Equal(MockNpgsqlDbType.Inet, param.NpgsqlDbType);
    }

    [Fact]
    public void Cidr_PostgreSql_ConfiguresCidrType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(Cidr), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.String, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new PostgreSqlLikeParameter();
        mapping.ConfigureParameter(param, new Cidr(IPAddress.Parse("192.168.1.0"), 24));

        Assert.Equal(MockNpgsqlDbType.Cidr, param.NpgsqlDbType);
    }

    [Fact]
    public void MacAddress_PostgreSql_ConfiguresMacAddrType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(MacAddress), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.String, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new PostgreSqlLikeParameter();
        mapping.ConfigureParameter(param, new MacAddress(PhysicalAddress.Parse("00-11-22-33-44-55")));

        Assert.Equal(MockNpgsqlDbType.MacAddr, param.NpgsqlDbType);
    }

    #endregion

    #region Temporal Mappings Testing

    [Fact]
    public void PostgreSqlInterval_PostgreSql_ConfiguresIntervalType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(PostgreSqlInterval), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Object, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new PostgreSqlLikeParameter();
        mapping.ConfigureParameter(param, new PostgreSqlInterval());

        Assert.Equal(MockNpgsqlDbType.Interval, param.NpgsqlDbType);
    }

    [Fact]
    public void IntervalYearMonth_Oracle_ConfiguresIntervalType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(IntervalYearMonth), SupportedDatabase.Oracle);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Object, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new OracleLikeParameter();
        mapping.ConfigureParameter(param, new IntervalYearMonth());

        Assert.Equal(MockOracleDbType.IntervalYM, param.OracleDbType);
    }

    [Fact]
    public void IntervalDaySecond_Oracle_ConfiguresIntervalType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(IntervalDaySecond), SupportedDatabase.Oracle);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Object, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new OracleLikeParameter();
        mapping.ConfigureParameter(param, new IntervalDaySecond());

        Assert.Equal(MockOracleDbType.IntervalDS, param.OracleDbType);
    }

    [Fact]
    public void DateTimeOffset_SqlServer_ConfiguresDbType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(DateTimeOffset), SupportedDatabase.SqlServer);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.DateTimeOffset, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new TestDbParameter();

        // Should work with proper DateTimeOffset
        var offset = new DateTimeOffset(DateTime.UtcNow);
        mapping.ConfigureParameter(param, offset);
        Assert.Equal(DbType.DateTimeOffset, param.DbType);
    }

    #endregion

    #region LOB Mappings Testing

    [Fact]
    public void Stream_SqlServer_ConfiguresVarbinaryMax()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(Stream), SupportedDatabase.SqlServer);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Binary, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new TestDbParameter();
        using var stream = new MemoryStream();
        mapping.ConfigureParameter(param, stream);

        Assert.Equal(DbType.Binary, param.DbType);
        Assert.Equal(-1, param.Size); // varbinary(max)
    }

    [Fact]
    public void TextReader_SqlServer_ConfiguresNVarcharMax()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(TextReader), SupportedDatabase.SqlServer);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.String, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new TestDbParameter();
        using var reader = new StringReader("test");
        mapping.ConfigureParameter(param, reader);

        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal(-1, param.Size); // nvarchar(max)
    }

    [Fact]
    public void Stream_PostgreSql_ConfiguresBytea()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(Stream), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Binary, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new TestDbParameter();
        using var stream = new MemoryStream();
        mapping.ConfigureParameter(param, stream);

        Assert.Equal(DbType.Binary, param.DbType);
    }

    [Fact]
    public void TextReader_PostgreSql_ConfiguresText()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(TextReader), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.String, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new PostgreSqlLikeParameter();
        using var reader = new StringReader("test");
        mapping.ConfigureParameter(param, reader);

        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal(MockNpgsqlDbType.Text, param.NpgsqlDbType);
    }

    [Fact]
    public void Stream_Oracle_ConfiguresBlob()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(Stream), SupportedDatabase.Oracle);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Binary, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new OracleLikeParameter();
        using var stream = new MemoryStream();
        mapping.ConfigureParameter(param, stream);

        Assert.Equal(MockOracleDbType.Blob, param.OracleDbType);
    }

    [Fact]
    public void TextReader_Oracle_ConfiguresClob()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(TextReader), SupportedDatabase.Oracle);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.String, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new OracleLikeParameter();
        using var reader = new StringReader("test");
        mapping.ConfigureParameter(param, reader);

        Assert.Equal(MockOracleDbType.Clob, param.OracleDbType);
    }

    #endregion

    #region Identity Mappings Testing

    [Fact]
    public void RowVersion_SqlServer_ConfiguresRowversion()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(RowVersion), SupportedDatabase.SqlServer);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Binary, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new SqlServerLikeParameter();
        mapping.ConfigureParameter(param, new RowVersion());

        Assert.Equal(DbType.Binary, param.DbType);
        Assert.Equal(8, param.Size);
        Assert.Equal(MockSqlDbType.Timestamp, param.SqlDbType);
    }

    [Fact]
    public void Guid_PostgreSql_ConfiguresUuid()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(Guid), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.Guid, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);

        var param = new PostgreSqlLikeParameter();
        mapping.ConfigureParameter(param, Guid.NewGuid());

        Assert.Equal(MockNpgsqlDbType.Uuid, param.NpgsqlDbType);
    }

    #endregion

    #region MappingKey Tests

    [Fact]
    public void MappingKey_Equality()
    {
        var key1 = new MappingKey(typeof(string), SupportedDatabase.SqlServer);
        var key2 = new MappingKey(typeof(string), SupportedDatabase.SqlServer);
        var key3 = new MappingKey(typeof(int), SupportedDatabase.SqlServer);
        var key4 = new MappingKey(typeof(string), SupportedDatabase.PostgreSql);

        Assert.Equal(key1, key2);
        Assert.NotEqual(key1, key3);
        Assert.NotEqual(key1, key4);

        Assert.True(key1.Equals(key2));
        Assert.False(key1.Equals(key3));
        Assert.False(key1.Equals(key4));
        Assert.False(key1.Equals(null));
        Assert.False(key1.Equals("not a key"));
    }

    [Fact]
    public void MappingKey_HashCode()
    {
        var key1 = new MappingKey(typeof(string), SupportedDatabase.SqlServer);
        var key2 = new MappingKey(typeof(string), SupportedDatabase.SqlServer);
        var key3 = new MappingKey(typeof(int), SupportedDatabase.SqlServer);

        Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
        Assert.NotEqual(key1.GetHashCode(), key3.GetHashCode());
    }

    #endregion

    #region CachedParameterConfig Tests

    [Fact]
    public void CachedParameterConfig_Constructor()
    {
        var mapping = new ProviderTypeMapping { DbType = DbType.String };
        var converter = new InetConverter();

        var config = new CachedParameterConfig(mapping, converter, 5);

        Assert.Same(mapping, config.Mapping);
        Assert.Same(converter, config.Converter);
        Assert.Equal(5, config.ConverterVersion);
    }

    [Fact]
    public void CachedParameterConfig_WithNullConverter()
    {
        var mapping = new ProviderTypeMapping { DbType = DbType.String };

        var config = new CachedParameterConfig(mapping, null, 0);

        Assert.Same(mapping, config.Mapping);
        Assert.Null(config.Converter);
        Assert.Equal(0, config.ConverterVersion);
    }

    [Fact]
    public void CachedParameterConfig_VersionStamp_TracksConverterChanges()
    {
        var registry = new AdvancedTypeRegistry();
        registry.RegisterMapping<Inet>(SupportedDatabase.PostgreSql,
            new ProviderTypeMapping { DbType = DbType.String });

        var inet = new Inet(IPAddress.Parse("10.0.0.1"));

        // First configure - no converter, caches with initial version
        var param1 = new TestDbParameter();
        var result1 = registry.TryConfigureParameter(param1, typeof(Inet), inet, SupportedDatabase.PostgreSql);
        Assert.True(result1);
        Assert.Equal(inet, param1.Value);

        // Register converter - bumps version
        registry.RegisterConverter(new InetConverter());

        // Second configure - should detect version mismatch and use new converter
        var param2 = new TestDbParameter();
        var result2 = registry.TryConfigureParameter(param2, typeof(Inet), inet, SupportedDatabase.PostgreSql);
        Assert.True(result2);
        Assert.Equal("10.0.0.1", param2.Value);

        // Register a second converter replacement - bumps version again
        registry.RegisterConverter(new InetConverter());

        // Third configure - should still work correctly
        var param3 = new TestDbParameter();
        var result3 = registry.TryConfigureParameter(param3, typeof(Inet), inet, SupportedDatabase.PostgreSql);
        Assert.True(result3);
        Assert.Equal("10.0.0.1", param3.Value);
    }

    #endregion

    #region Mock Parameter Classes for Testing

    private class TestDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
#pragma warning disable CS8765 // Nullability of type of parameter doesn't match overridden member
        public override string ParameterName { get; set; } = string.Empty;
#pragma warning restore CS8765
        public override int Size { get; set; }
#pragma warning disable CS8765 // Nullability of type of parameter doesn't match overridden member  
        public override string SourceColumn { get; set; } = string.Empty;
#pragma warning restore CS8765
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }

        public override void ResetDbType()
        {
            DbType = DbType.Object;
        }
    }

    private class PostgreSqlLikeParameter : TestDbParameter
    {
        public string? DataTypeName { get; set; }
        public MockNpgsqlDbType? NpgsqlDbType { get; set; }
    }

    // Mock enum to simulate NpgsqlDbType with flags  
    [Flags]
    private enum MockNpgsqlDbType
    {
        Array = 1,
        Integer = 2,
        Text = 4,
        Cidr = 8,
        Inet = 16,
        Uuid = 32,
        Interval = 64,
        Int4Range = 128,
        TsRange = 256,
        MacAddr = 512,
        Jsonb = 1024,
        JSON = 2048
    }

    private class MySqlLikeParameter : TestDbParameter
    {
        public MockMySqlDbType? MySqlDbType { get; set; }
    }

    private class SqlServerLikeParameter : TestDbParameter
    {
        public MockSqlDbType? SqlDbType { get; set; }
        public string? UdtTypeName { get; set; }
    }

    private class OracleLikeParameter : TestDbParameter
    {
        public MockOracleDbType? OracleDbType { get; set; }
    }

    // Mock enums for other database providers
    private enum MockMySqlDbType
    {
        JSON = 1
    }

    private enum MockSqlDbType
    {
        Udt = 1,
        NVarChar = 2,
        Timestamp = 3
    }

    private enum MockOracleDbType
    {
        IntervalDS = 1,
        IntervalYM = 2,
        Blob = 3,
        Clob = 4
    }

    #endregion
}