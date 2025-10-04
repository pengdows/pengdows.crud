using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.types;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class AdvancedTypeRegistryTests
{
    [Fact]
    public void Shared_ShouldReturnSingletonInstance()
    {
        var registry1 = AdvancedTypeRegistry.Shared;
        var registry2 = AdvancedTypeRegistry.Shared;

        Assert.Same(registry1, registry2);
    }

    [Fact]
    public void RegisterMapping_ShouldStoreMapping()
    {
        var registry = new AdvancedTypeRegistry();
        var mapping = new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) => param.Size = 255
        };

        registry.RegisterMapping<string>(SupportedDatabase.PostgreSql, mapping);
        var retrieved = registry.GetMapping(typeof(string), SupportedDatabase.PostgreSql);

        Assert.Same(mapping, retrieved);
    }

    [Fact]
    public void GetMapping_ShouldReturnNullForUnregisteredType()
    {
        var registry = new AdvancedTypeRegistry();
        var mapping = registry.GetMapping(typeof(int), SupportedDatabase.MySql);

        Assert.Null(mapping);
    }

    [Fact]
    public void RegisterConverter_ShouldStoreConverter()
    {
        var registry = new AdvancedTypeRegistry();
        var converter = new InetConverter();

        registry.RegisterConverter(converter);
        var retrieved = registry.GetConverter(typeof(Inet));

        Assert.Same(converter, retrieved);
    }

    [Fact]
    public void GetConverter_ShouldReturnNullForUnregisteredType()
    {
        var registry = new AdvancedTypeRegistry();
        var converter = registry.GetConverter(typeof(DateTime));

        Assert.Null(converter);
    }

    [Fact]
    public void TryConfigureParameter_ShouldApplyMappingAndConverter()
    {
        var registry = new AdvancedTypeRegistry();
        var inet = new Inet(System.Net.IPAddress.Parse("192.168.1.1"));

        // Register mapping for Inet type
        var mapping = new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) => param.Size = 45 // Max IPv6 length
        };
        registry.RegisterMapping<Inet>(SupportedDatabase.PostgreSql, mapping);

        // Register converter for Inet type
        var converter = new InetConverter();
        registry.RegisterConverter(converter);

        // Create a mock parameter
        var parameter = new TestDbParameter();
        var success = registry.TryConfigureParameter(parameter, typeof(Inet), inet, SupportedDatabase.PostgreSql);

        Assert.True(success);
        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("192.168.1.1", parameter.Value); // Converted by InetConverter
        Assert.Equal(45, parameter.Size); // Applied by mapping
    }

    [Fact]
    public void TryConfigureParameter_ShouldHandleNullValue()
    {
        var registry = new AdvancedTypeRegistry();
        var mapping = new ProviderTypeMapping
        {
            DbType = DbType.String
        };
        registry.RegisterMapping<string>(SupportedDatabase.SqlServer, mapping);

        var parameter = new TestDbParameter();
        var success = registry.TryConfigureParameter(parameter, typeof(string), null, SupportedDatabase.SqlServer);

        Assert.True(success);
        Assert.Equal(DBNull.Value, parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    [Fact]
    public void TryConfigureParameter_ShouldFailForUnregisteredType()
    {
        var registry = new AdvancedTypeRegistry();
        var parameter = new TestDbParameter();

        var success = registry.TryConfigureParameter(parameter, typeof(Guid), Guid.NewGuid(), SupportedDatabase.SqlServer);

        Assert.False(success);
    }

    [Fact]
    public void DefaultMappings_ShouldIncludeJsonDocument()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(JsonDocument), SupportedDatabase.PostgreSql);

        Assert.NotNull(mapping);
        Assert.Equal(DbType.String, mapping.DbType);
        Assert.NotNull(mapping.ConfigureParameter);
    }

    [Fact]
    public void DefaultMappings_ShouldIncludeSpatialTypes()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var geometryMapping = registry.GetMapping(typeof(Geometry), SupportedDatabase.SqlServer);
        var geographyMapping = registry.GetMapping(typeof(Geography), SupportedDatabase.SqlServer);

        Assert.NotNull(geometryMapping);
        Assert.NotNull(geographyMapping);
        Assert.Equal(DbType.Object, geometryMapping.DbType);
        Assert.Equal(DbType.Object, geographyMapping.DbType);
    }

    [Fact]
    public void DefaultMappings_ShouldIncludeNetworkTypes()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var inetMapping = registry.GetMapping(typeof(Inet), SupportedDatabase.PostgreSql);
        var cidrMapping = registry.GetMapping(typeof(Cidr), SupportedDatabase.PostgreSql);
        var macMapping = registry.GetMapping(typeof(MacAddress), SupportedDatabase.PostgreSql);

        Assert.NotNull(inetMapping);
        Assert.NotNull(cidrMapping);
        Assert.NotNull(macMapping);
        Assert.Equal(DbType.String, inetMapping.DbType);
        Assert.Equal(DbType.String, cidrMapping.DbType);
        Assert.Equal(DbType.String, macMapping.DbType);
    }

    [Fact]
    public void DefaultMappings_ShouldIncludeArrayTypes()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var intArrayMapping = registry.GetMapping(typeof(int[]), SupportedDatabase.PostgreSql);
        var stringArrayMapping = registry.GetMapping(typeof(string[]), SupportedDatabase.PostgreSql);

        Assert.NotNull(intArrayMapping);
        Assert.NotNull(stringArrayMapping);
        Assert.Equal(DbType.Object, intArrayMapping.DbType);
        Assert.Equal(DbType.Object, stringArrayMapping.DbType);
    }

    [Fact]
    public void DefaultMappings_ShouldIncludeRangeTypes()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var intRangeMapping = registry.GetMapping(typeof(Range<int>), SupportedDatabase.PostgreSql);
        var dateRangeMapping = registry.GetMapping(typeof(Range<DateTime>), SupportedDatabase.PostgreSql);

        Assert.NotNull(intRangeMapping);
        Assert.NotNull(dateRangeMapping);
        Assert.Equal(DbType.String, intRangeMapping.DbType);
        Assert.Equal(DbType.String, dateRangeMapping.DbType);
    }

    [Fact]
    public void DefaultMappings_ShouldIncludeRowVersionType()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var rowVersionMapping = registry.GetMapping(typeof(RowVersion), SupportedDatabase.SqlServer);

        Assert.NotNull(rowVersionMapping);
        Assert.Equal(DbType.Binary, rowVersionMapping.DbType);
    }

    [Fact]
    public void DefaultConverters_ShouldBeRegistered()
    {
        var registry = AdvancedTypeRegistry.Shared;

        Assert.NotNull(registry.GetConverter(typeof(Geometry)));
        Assert.NotNull(registry.GetConverter(typeof(Geography)));
        Assert.NotNull(registry.GetConverter(typeof(Inet)));
        Assert.NotNull(registry.GetConverter(typeof(Cidr)));
        Assert.NotNull(registry.GetConverter(typeof(MacAddress)));
        Assert.NotNull(registry.GetConverter(typeof(PostgreSqlInterval)));
        Assert.NotNull(registry.GetConverter(typeof(IntervalYearMonth)));
        Assert.NotNull(registry.GetConverter(typeof(IntervalDaySecond)));
        Assert.NotNull(registry.GetConverter(typeof(RowVersion)));
        Assert.NotNull(registry.GetConverter(typeof(Stream)));
        Assert.NotNull(registry.GetConverter(typeof(TextReader)));
    }

    // Mock parameter class for testing
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