using System;
using pengdows.crud.types.attributes;
using Xunit;

namespace pengdows.crud.Tests;

public class WeirdTypeAttributesTests
{
    [Fact]
    public void DbEnumAttribute_DefaultsCanBeOverridden()
    {
        var attr = new DbEnumAttribute
        {
            AllowUnknown = true,
            StoreAs = EnumStorage.Int
        };

        Assert.True(attr.AllowUnknown);
        Assert.Equal(EnumStorage.Int, attr.StoreAs);
    }

    [Fact]
    public void JsonContractAttribute_RequiresShapeType()
    {
        Assert.Throws<ArgumentNullException>(() => new JsonContractAttribute(null!));

        var attr = new JsonContractAttribute(typeof(string));
        Assert.Equal(typeof(string), attr.ShapeType);
    }

    [Fact]
    public void RangeTypeAttribute_AllowsSettingCanonicalFormat()
    {
        var attr = new RangeTypeAttribute { CanonicalFormat = "[a,b)" };
        Assert.Equal("[a,b)", attr.CanonicalFormat);
    }

    [Fact]
    public void ComputedAttribute_DefaultStoredIsFalse()
    {
        var attr = new ComputedAttribute();
        Assert.False(attr.Stored);

        attr.Stored = true;
        Assert.True(attr.Stored);
    }

    [Fact]
    public void MaxLengthForInlineAttribute_ValidatesInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaxLengthForInlineAttribute(0));

        var attr = new MaxLengthForInlineAttribute(128);
        Assert.Equal(128, attr.MaxLength);
    }

    [Fact]
    public void SpatialTypeAttribute_Defaults()
    {
        var attr = new SpatialTypeAttribute();
        Assert.Equal(-1, attr.ExpectedSrid);
        Assert.False(attr.AllowGeometryGeographyConversion);

        attr.ExpectedSrid = 4326;
        attr.AllowGeometryGeographyConversion = true;
        Assert.Equal(4326, attr.ExpectedSrid);
        Assert.True(attr.AllowGeometryGeographyConversion);
    }

    [Fact]
    public void CurrencyAttribute_StoresCode()
    {
        Assert.Throws<ArgumentNullException>(() => new CurrencyAttribute(null!));

        var attr = new CurrencyAttribute("USD");
        Assert.Equal("USD", attr.CurrencyCode);
    }

    [Fact]
    public void MarkerAttributes_ConstructSuccessfully()
    {
        Assert.NotNull(new ConcurrencyTokenAttribute());
        Assert.NotNull(new CaseInsensitiveAttribute());
        Assert.NotNull(new AsStringAttribute());
        Assert.NotNull(new AllowZeroDateAttribute());
        Assert.NotNull(new CaseFoldOnReadAttribute());
    }
}
