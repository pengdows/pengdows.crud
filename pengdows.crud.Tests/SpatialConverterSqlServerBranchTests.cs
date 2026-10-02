using System;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class SpatialConverterSqlServerBranchTests
{

    [Fact]
    public void TryConvertFromProvider_UnknownObject_ReturnsFalseAndNull()
    {
        var converter = new TestSpatialConverter();

        var success = converter.TryConvertFromProvider(new object(), SupportedDatabase.Sqlite, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToProvider_SqlServer_Wkb_IsBigEndianSridThenWkb()
    {
        var wkb = WellKnownTextEncoder.Encode("POINT(1 2)");
        var value = Geometry.FromWellKnownBinary(wkb, 4326);

        var providerValue = Assert.IsType<byte[]>(new GeometryConverter().ToProviderValue(value, SupportedDatabase.SqlServer));

        Assert.Equal(4326, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(providerValue));
        Assert.Equal(wkb, providerValue[4..]);
    }

    [Fact]
    public void ConvertToProvider_SqlServer_Wkt_EncodesTheTextAsWkb()
    {
        var value = Geometry.FromWellKnownText("POINT(1 2)", 3857);

        var providerValue = Assert.IsType<byte[]>(new GeometryConverter().ToProviderValue(value, SupportedDatabase.SqlServer));

        Assert.Equal(3857, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(providerValue));
        Assert.Equal(WellKnownTextEncoder.Encode("POINT(1 2)"), providerValue[4..]);
    }

    [Fact]
    public void ConvertToProvider_SqlServer_Geography_IsBigEndianSridThenWkb()
    {
        var value = Geography.FromWellKnownText("POINT(3 4)", 4326);

        var providerValue = Assert.IsType<byte[]>(new GeographyConverter().ToProviderValue(value, SupportedDatabase.SqlServer));

        Assert.Equal(4326, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(providerValue));
        Assert.Equal(WellKnownTextEncoder.Encode("POINT(3 4)"), providerValue[4..]);
    }

    [Fact]
    public void ConvertToProvider_SqlServer_NoTextOrBinary_ThrowsNotSupportedException()
    {
        var converter = new GeometryConverter();
        var ctor = typeof(Geometry).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            new[]
            {
                typeof(int),
                typeof(SpatialFormat),
                typeof(ReadOnlyMemory<byte>),
                typeof(string),
                typeof(string),
                typeof(object)
            },
            modifiers: null) ?? throw new InvalidOperationException("Geometry constructor was not found.");

        var invalid = (Geometry)ctor.Invoke(new object?[]
        {
            4326,
            SpatialFormat.WellKnownText,
            ReadOnlyMemory<byte>.Empty,
            null,
            null,
            null
        });

        var ex = Assert.ThrowsAny<Exception>(() => converter.ToProviderValue(invalid, SupportedDatabase.SqlServer));
        var root = Assert.IsType<NotSupportedException>(ex.GetBaseException());
        Assert.Contains("WKB or WKT", root.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestSpatialConverter : SpatialConverter<Geometry>
    {
        protected override Geometry FromBinary(ReadOnlySpan<byte> wkb, SupportedDatabase provider)
        {
            return Geometry.FromWellKnownBinary(wkb, 4326);
        }

        protected override Geometry FromTextInternal(string text, SupportedDatabase provider)
        {
            return Geometry.FromWellKnownText(text, 4326);
        }

        protected override Geometry FromGeoJsonInternal(string json, SupportedDatabase provider)
        {
            return Geometry.FromGeoJson(json, 4326);
        }

        protected override Geometry WrapWithProvider(Geometry spatial, object providerValue)
        {
            return spatial.WithProviderValue(providerValue);
        }

        protected override Geometry FromBinaryWithSrid(ReadOnlySpan<byte> wkb, int srid, object providerValue)
        {
            return Geometry.FromWellKnownBinary(wkb, srid, providerValue);
        }
    }
}
