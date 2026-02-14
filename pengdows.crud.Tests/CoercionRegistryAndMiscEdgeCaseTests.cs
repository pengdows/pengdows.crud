using System;
using System.Data;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Moq;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests edge cases for CoercionRegistry, BasicCoercions (JsonValue, DateTimeOffset, StringArray),
/// AdvancedCoercions (ClobStream, Cidr, MacAddress), and Geography/Geometry factory methods.
/// </summary>
public class CoercionRegistryAndMiscEdgeCaseTests
{
    // ===== CoercionRegistry =====

    [Fact]
    public void CoercionRegistry_TryWrite_NullValue_SetsDBNull()
    {
        var registry = new CoercionRegistry();
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        var success = registry.TryWrite(null, param.Object);
        Assert.True(success);
        param.VerifySet(p => p.Value = DBNull.Value, Times.Once);
    }

    [Fact]
    public void CoercionRegistry_TryWrite_UnregisteredType_ReturnsFalse()
    {
        var registry = new CoercionRegistry();
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        // Use a type that's definitely not registered
        var success = registry.TryWrite(new UnregisteredType(), param.Object);
        Assert.False(success);
    }

    [Fact]
    public void CoercionRegistry_TryRead_UnregisteredType_ReturnsFalse()
    {
        var registry = new CoercionRegistry();
        var dbValue = new DbValue("test");

        var success = registry.TryRead(dbValue, typeof(UnregisteredType), out var value);
        Assert.False(success);
        Assert.Null(value);
    }

    [Fact]
    public void CoercionRegistry_ProviderSpecific_PrefersOverGeneral()
    {
        var registry = new CoercionRegistry();

        // Register a general coercion and a provider-specific one
        var generalCoercion = new TestCoercion("general");
        var providerCoercion = new TestCoercion("provider");

        registry.Register<TestRegisteredType>(generalCoercion);
        registry.Register<TestRegisteredType>(SupportedDatabase.PostgreSql, providerCoercion);

        // Without provider, gets general
        var general = registry.GetCoercion(typeof(TestRegisteredType));
        Assert.NotNull(general);

        // With provider, gets provider-specific
        var specific = registry.GetCoercion(typeof(TestRegisteredType), SupportedDatabase.PostgreSql);
        Assert.NotNull(specific);
        Assert.NotSame(general, specific);

        // With a different provider, falls back to general
        var fallback = registry.GetCoercion(typeof(TestRegisteredType), SupportedDatabase.MySql);
        Assert.Same(general, fallback);
    }

    [Fact]
    public void CoercionRegistry_TryRead_RegisteredType_Succeeds()
    {
        var registry = new CoercionRegistry();
        var guid = Guid.NewGuid();
        var dbValue = new DbValue(guid);

        var success = registry.TryRead(dbValue, typeof(Guid), out var value);
        Assert.True(success);
        Assert.Equal(guid, value);
    }

    [Fact]
    public void CoercionRegistry_TryWrite_RegisteredType_Succeeds()
    {
        var registry = new CoercionRegistry();
        var guid = Guid.NewGuid();
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        var success = registry.TryWrite(guid, param.Object);
        Assert.True(success);
    }

    // ===== JsonValueCoercion =====

    [Fact]
    public void JsonValueCoercion_TryRead_JsonElement_ReturnsTrue()
    {
        var coercion = new JsonValueCoercion();
        var doc = JsonDocument.Parse("{\"key\":\"value\"}");
        var element = doc.RootElement;

        Assert.True(coercion.TryRead(new DbValue(element), out var result));
        Assert.NotEqual(default, result);
    }

    [Fact]
    public void JsonValueCoercion_TryRead_JsonDocument_ReturnsTrue()
    {
        var coercion = new JsonValueCoercion();
        var doc = JsonDocument.Parse("{\"key\":\"value\"}");

        Assert.True(coercion.TryRead(new DbValue(doc), out var result));
        Assert.NotEqual(default, result);
    }

    [Fact]
    public void JsonValueCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new JsonValueCoercion();

        Assert.False(coercion.TryRead(new DbValue(42), out _));
    }

    [Fact]
    public void JsonValueCoercion_TryRead_MalformedJson_ReturnsFalse()
    {
        var coercion = new JsonValueCoercion();

        Assert.False(coercion.TryRead(new DbValue("not valid json {{{", typeof(string)), out _));
    }

    [Fact]
    public void JsonValueCoercion_TryRead_ValidJson_ReturnsTrue()
    {
        var coercion = new JsonValueCoercion();

        Assert.True(coercion.TryRead(new DbValue("{\"a\":1}", typeof(string)), out var result));
        Assert.NotEqual(default, result);
    }

    [Fact]
    public void JsonValueCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new JsonValueCoercion();

        Assert.False(coercion.TryRead(new DbValue(null), out _));
    }

    // ===== DateTimeOffsetCoercion =====

    [Fact]
    public void DateTimeOffsetCoercion_TryRead_DateTime_Converts()
    {
        var coercion = new DateTimeOffsetCoercion();
        var dt = new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(coercion.TryRead(new DbValue(dt), out var result));
        Assert.Equal(new DateTimeOffset(dt), result);
    }

    [Fact]
    public void DateTimeOffsetCoercion_TryRead_DateTimeOffset_Passthrough()
    {
        var coercion = new DateTimeOffsetCoercion();
        var dto = new DateTimeOffset(2023, 6, 15, 12, 0, 0, TimeSpan.FromHours(5));

        Assert.True(coercion.TryRead(new DbValue(dto), out var result));
        Assert.Equal(dto, result);
    }

    [Fact]
    public void DateTimeOffsetCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new DateTimeOffsetCoercion();

        Assert.False(coercion.TryRead(new DbValue("not a date"), out _));
    }

    [Fact]
    public void DateTimeOffsetCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new DateTimeOffsetCoercion();

        Assert.False(coercion.TryRead(new DbValue(null), out _));
    }

    [Fact]
    public void DateTimeOffsetCoercion_TryWrite_SetsValue()
    {
        var coercion = new DateTimeOffsetCoercion();
        var dto = DateTimeOffset.UtcNow;
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(dto, param.Object));
        param.VerifySet(p => p.Value = dto, Times.Once);
        param.VerifySet(p => p.DbType = DbType.DateTimeOffset, Times.Once);
    }

    // ===== StringArrayCoercion =====

    [Fact]
    public void StringArrayCoercion_TryRead_NullDbValue_ReturnsFalse()
    {
        var coercion = new StringArrayCoercion();

        Assert.False(coercion.TryRead(new DbValue(null), out _));
    }

    [Fact]
    public void StringArrayCoercion_TryRead_StringArray_ReturnsTrue()
    {
        var coercion = new StringArrayCoercion();
        var arr = new[] { "a", "b", "c" };

        Assert.True(coercion.TryRead(new DbValue(arr), out var result));
        Assert.Equal(arr, result);
    }

    [Fact]
    public void StringArrayCoercion_TryRead_NonStringArray_ReturnsFalse()
    {
        var coercion = new StringArrayCoercion();

        Assert.False(coercion.TryRead(new DbValue("not an array"), out _));
    }

    [Fact]
    public void StringArrayCoercion_TryWrite_NullArray_SetsDBNull()
    {
        var coercion = new StringArrayCoercion();
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(null, param.Object));
        param.VerifySet(p => p.Value = DBNull.Value, Times.Once);
    }

    [Fact]
    public void StringArrayCoercion_TryWrite_ValidArray_SetsValue()
    {
        var coercion = new StringArrayCoercion();
        var arr = new[] { "a", "b" };
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(arr, param.Object));
        param.VerifySet(p => p.Value = arr, Times.Once);
    }

    // ===== ClobStreamCoercion =====

    [Fact]
    public void ClobStreamCoercion_TryRead_TextReaderPassthrough()
    {
        var coercion = new ClobStreamCoercion();
        var reader = new StringReader("test");

        Assert.True(coercion.TryRead(new DbValue(reader), out var result));
        Assert.Same(reader, result);
    }

    [Fact]
    public void ClobStreamCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new ClobStreamCoercion();

        Assert.False(coercion.TryRead(new DbValue(42), out _));
    }

    [Fact]
    public void ClobStreamCoercion_TryWrite_NullValue_SetsValue()
    {
        var coercion = new ClobStreamCoercion();
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(null, param.Object));
        param.VerifySet(p => p.DbType = DbType.String, Times.Once);
    }

    // ===== CidrCoercion =====

    [Fact]
    public void CidrCoercion_TryRead_InvalidString_ReturnsFalse()
    {
        var coercion = new CidrCoercion();

        Assert.False(coercion.TryRead(new DbValue("not a cidr", typeof(string)), out _));
    }

    [Fact]
    public void CidrCoercion_TryRead_CidrPassthrough_ReturnsTrue()
    {
        var coercion = new CidrCoercion();
        var cidr = Cidr.Parse("10.0.0.0/8");

        Assert.True(coercion.TryRead(new DbValue(cidr), out var result));
        Assert.Equal(cidr, result);
    }

    [Fact]
    public void CidrCoercion_TryRead_ProviderShim_MissingProps_ReturnsFalse()
    {
        var coercion = new CidrCoercion();
        // Object with Cidr in name but missing expected properties
        var shim = new FakeCidrShimNoProps();

        Assert.False(coercion.TryRead(new DbValue(shim), out _));
    }

    [Fact]
    public void CidrCoercion_TryRead_ProviderShim_ValidProps_ReturnsTrue()
    {
        var coercion = new CidrCoercion();
        var shim = new FakeCidrShimWithProps(IPAddress.Parse("192.168.0.0"), (byte)24);

        Assert.True(coercion.TryRead(new DbValue(shim), out var result));
        Assert.Equal("192.168.0.0/24", result.ToString());
    }

    [Fact]
    public void CidrCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new CidrCoercion();

        Assert.False(coercion.TryRead(new DbValue(null), out _));
    }

    // ===== MacAddressCoercion =====

    [Fact]
    public void MacAddressCoercion_TryRead_InvalidString_ReturnsFalse()
    {
        var coercion = new MacAddressCoercion();

        Assert.False(coercion.TryRead(new DbValue("not a mac", typeof(string)), out _));
    }

    [Fact]
    public void MacAddressCoercion_TryRead_MacPassthrough_ReturnsTrue()
    {
        var coercion = new MacAddressCoercion();
        var mac = MacAddress.Parse("08:00:2B:01:02:03");

        Assert.True(coercion.TryRead(new DbValue(mac), out var result));
        Assert.Equal(mac, result);
    }

    [Fact]
    public void MacAddressCoercion_TryRead_PhysicalAddress_ReturnsTrue()
    {
        var coercion = new MacAddressCoercion();
        var physical = new PhysicalAddress(new byte[] { 0x08, 0x00, 0x2B, 0x01, 0x02, 0x03 });

        Assert.True(coercion.TryRead(new DbValue(physical), out var result));
        Assert.Equal(physical, result.Address);
    }

    [Fact]
    public void MacAddressCoercion_TryRead_ProviderShim_NullAddress_ReturnsFalse()
    {
        var coercion = new MacAddressCoercion();
        var shim = new FakeMacAddressShim(null);

        Assert.False(coercion.TryRead(new DbValue(shim), out _));
    }

    [Fact]
    public void MacAddressCoercion_TryRead_ProviderShim_ValidAddress_ReturnsTrue()
    {
        var coercion = new MacAddressCoercion();
        var physical = new PhysicalAddress(new byte[] { 0x08, 0x00, 0x2B, 0x01, 0x02, 0x03 });
        var shim = new FakeMacAddressShim(physical);

        Assert.True(coercion.TryRead(new DbValue(shim), out var result));
        Assert.Equal(physical, result.Address);
    }

    [Fact]
    public void MacAddressCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new MacAddressCoercion();

        Assert.False(coercion.TryRead(new DbValue(42), out _));
    }

    // ===== Geography factory validation =====

    [Fact]
    public void Geography_FromWellKnownText_EmptyWkt_Throws()
    {
        Assert.Throws<ArgumentException>(() => Geography.FromWellKnownText("", 4326));
        Assert.Throws<ArgumentException>(() => Geography.FromWellKnownText("  ", 4326));
    }

    [Fact]
    public void Geography_FromGeoJson_EmptyGeoJson_Throws()
    {
        Assert.Throws<ArgumentException>(() => Geography.FromGeoJson("", 4326));
        Assert.Throws<ArgumentException>(() => Geography.FromGeoJson("  ", 4326));
    }

    [Fact]
    public void Geography_FromWellKnownBinary_Succeeds()
    {
        var wkb = new byte[] { 1, 2, 3 };
        var result = Geography.FromWellKnownBinary(wkb, 4326);
        Assert.Equal(wkb, result.WellKnownBinary.ToArray());
        Assert.Equal(4326, result.Srid);
    }

    [Fact]
    public void Geography_WithProviderValue_ClonesCorrectly()
    {
        var original = Geography.FromWellKnownText("POINT(1 2)", 4326);
        var provider = new object();
        var cloned = original.WithProviderValue(provider);

        Assert.Same(provider, cloned.ProviderValue);
        Assert.Equal(original.WellKnownText, cloned.WellKnownText);
        Assert.Equal(original.Srid, cloned.Srid);
    }

    // ===== Geometry factory validation =====

    [Fact]
    public void Geometry_FromWellKnownText_EmptyWkt_Throws()
    {
        Assert.Throws<ArgumentException>(() => Geometry.FromWellKnownText("", 0));
        Assert.Throws<ArgumentException>(() => Geometry.FromWellKnownText("  ", 0));
    }

    [Fact]
    public void Geometry_FromGeoJson_EmptyGeoJson_Throws()
    {
        Assert.Throws<ArgumentException>(() => Geometry.FromGeoJson("", 0));
        Assert.Throws<ArgumentException>(() => Geometry.FromGeoJson("  ", 0));
    }

    [Fact]
    public void Geometry_FromWellKnownBinary_Succeeds()
    {
        var wkb = new byte[] { 1, 2, 3 };
        var result = Geometry.FromWellKnownBinary(wkb, 0);
        Assert.Equal(wkb, result.WellKnownBinary.ToArray());
        Assert.Equal(0, result.Srid);
    }

    [Fact]
    public void Geometry_WithProviderValue_ClonesCorrectly()
    {
        var original = Geometry.FromWellKnownText("POINT(1 2)", 0);
        var provider = new object();
        var cloned = original.WithProviderValue(provider);

        Assert.Same(provider, cloned.ProviderValue);
        Assert.Equal(original.WellKnownText, cloned.WellKnownText);
        Assert.Equal(original.Srid, cloned.Srid);
    }

    // ===== Helper types =====

    private class UnregisteredType
    {
    }

    private struct TestRegisteredType
    {
    }

    /// <summary>
    /// Test coercion for verifying provider-specific preference.
    /// </summary>
    private class TestCoercion : DbCoercion<TestRegisteredType>
    {
        private readonly string _name;

        public TestCoercion(string name)
        {
            _name = name;
        }

        public override bool TryRead(in DbValue src, out TestRegisteredType value)
        {
            value = default;
            return !src.IsNull;
        }

        public override bool TryWrite(TestRegisteredType value, System.Data.Common.DbParameter parameter)
        {
            parameter.Value = _name;
            return true;
        }
    }

    /// <summary>
    /// Shim with "Cidr" in name but no Address/Netmask properties.
    /// </summary>
    private class FakeCidrShimNoProps
    {
    }

    /// <summary>
    /// Shim with "Cidr" in name and proper Address/Netmask properties.
    /// </summary>
    private class FakeCidrShimWithProps
    {
        public FakeCidrShimWithProps(IPAddress? address, byte netmask)
        {
            Address = address;
            Netmask = netmask;
        }

        public IPAddress? Address { get; }
        public byte Netmask { get; }
    }

    /// <summary>
    /// Shim with "MacAddress" in name for reflection-based provider detection.
    /// </summary>
    private class FakeMacAddressShim
    {
        public FakeMacAddressShim(PhysicalAddress? address)
        {
            Address = address;
        }

        public PhysicalAddress? Address { get; }
    }
}
