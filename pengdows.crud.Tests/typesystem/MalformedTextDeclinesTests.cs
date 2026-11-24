using System;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Characterization for the review's catch-all narrowing (Ring 2): each coercion that used to catch everything and return
/// false must still decline malformed input the same way. These pass before and after the narrowing; they pin the contract
/// the narrowed catches must keep.
/// </summary>
public class MalformedTextDeclinesTests
{
    [Fact]
    public void IntArrayCoercion_MalformedElement_Declines()
    {
        var src = new DbValue("1,x", typeof(string));

        Assert.False(new IntArrayCoercion().TryRead(in src, out _));
    }

    [Fact]
    public void HStoreCoercion_PairWithoutArrow_Declines()
    {
        var src = new DbValue("novalue", typeof(string));

        Assert.False(new HStoreCoercion().TryRead(in src, out _));
    }

    [Fact]
    public void DecimalCoercion_DateTime_Declines()
    {
        var src = new DbValue(new DateTime(2026, 1, 1), typeof(DateTime));

        Assert.False(new DecimalCoercion().TryRead(in src, out _));
    }

    [Fact]
    public void InetConverter_MalformedAddress_Declines()
    {
        Assert.False(new InetConverter().TryConvertFromProvider("not an address", SupportedDatabase.PostgreSql, out _));
    }

    [Fact]
    public void IntArrayCoercion_ElementOutOfIntRange_Declines()
    {
        var src = new DbValue("99999999999", typeof(string));

        Assert.False(new IntArrayCoercion().TryRead(in src, out _));
    }

    [Fact]
    public void HStoreCoercion_OnePairGoodOneMalformed_Declines()
    {
        var src = new DbValue("a=>b,novalue", typeof(string));

        Assert.False(new HStoreCoercion().TryRead(in src, out _));
    }

    [Fact]
    public void InetConverter_PrefixOutOfRange_Declines()
    {
        // Inet's constructor throws ArgumentOutOfRangeException for a prefix past the family's maximum.
        Assert.False(new InetConverter().TryConvertFromProvider("10.0.0.1/99", SupportedDatabase.PostgreSql, out _));
    }

    [Fact]
    public void RowVersionConverter_WrongLengthBytes_IsRefused()
    {
        var converter = new RowVersionConverter();
        var accepted = converter.TryConvertFromProvider(new byte[] { 1, 2, 3 }, SupportedDatabase.SqlServer, out _);

        Assert.False(accepted);
    }
}
