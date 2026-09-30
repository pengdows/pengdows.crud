using System;
using System.Linq;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-016. Every vector below was produced live by SQL Server 2025 (17.0.5005.3):
/// CAST(CAST(path AS hierarchyid) AS varbinary(900)), hierarchyid::ToString() and GetLevel(), one
/// per numeric range of the OrdPath encoding plus dotted and multi-level paths. The sort order is
/// the server's ORDER BY over the same values.
/// </summary>
public sealed class HierarchyIdTests
{
    public static TheoryData<string, string, int> SqlServerVectors => new()
    {
        { "/", "", 0 },
        { "/0/", "48", 1 },
        { "/1/", "58", 1 },
        { "/3/", "78", 1 },
        { "/4/", "84", 1 },
        { "/7/", "9C", 1 },
        { "/8/", "A2", 1 },
        { "/15/", "BE", 1 },
        { "/16/", "C110", 1 },
        { "/79/", "DBF0", 1 },
        { "/80/", "E00440", 1 },
        { "/1103/", "EEEFC0", 1 },
        { "/1104/", "F00088", 1 },
        { "/5199/", "F7DDF8", 1 },
        { "/5200/", "F80000000220", 1 },
        { "/4294972495/", "FBFFFFBF77E0", 1 },
        { "/4294972496/", "FC00000000000110", 1 },
        { "/281479271683151/", "FFFFF7FFFFDFBBF0", 1 },
        { "/-1/", "3F80", 1 },
        { "/-8/", "3880", 1 },
        { "/-9/", "2DF8", 1 },
        { "/-72/", "2088", 1 },
        { "/-73/", "1BEEFC", 1 },
        { "/-4168/", "180044", 1 },
        { "/-4169/", "17FFFFBF77E0", 1 },
        { "/-4294971464/", "140000000220", 1 },
        { "/-4294971465/", "13FFF7FFFFDFBBF0", 1 },
        { "/-281479271682120/", "1000000000000110", 1 },
        { "/42/", "CB50", 1 },
        { "/1000/", "EE2C40", 1 },
        { "/123456789012/", "FC00677D3205A190", 1 },
        { "/1/2/", "5B40", 2 },
        { "/1/1/", "5AC0", 2 },
        { "/1.1/", "62C0", 1 },
        { "/1.-1/", "61FC", 1 },
        { "/0.0/", "5240", 1 },
        { "/3/4.5/", "7C5180", 2 },
        { "/1/2.3.4/", "5BA084", 2 },
        { "/-1.5/7/", "447380", 2 },
        { "/5200.1/", "F8000000024B", 1 },
        { "/79.80/1103/", "E00438011EEEFC", 2 },
        { "/2.-8/-72.4/", "71C4825080", 2 },
        { "/1/2/3/4/5/6/7/8/", "5B5F0C72CF44", 8 },
        { "/281479271683150.0/", "FFFFF7FFFFDFBBE480", 1 },
        { "/-281479271682120.5/", "1000000000000128C0", 1 },
        { "/0.-281479271682120/", "508000000000000880", 1 },
    };

    private static readonly string[] SqlServerOrder =
    {
        "/", "/-281479271682120/", "/-281479271682120.5/", "/-4294971465/", "/-4294971464/", "/-4169/", "/-4168/", "/-73/", "/-72/", "/-9/", "/-8/", "/-1/", "/-1.5/7/", "/0/", "/0.-281479271682120/", "/0.0/", "/1/", "/1/1/", "/1/2/", "/1/2/3/4/5/6/7/8/", "/1/2.3.4/", "/1.-1/", "/1.1/", "/2.-8/-72.4/", "/3/", "/3/4.5/", "/4/", "/7/", "/8/", "/15/", "/16/", "/42/", "/79/", "/79.80/1103/", "/80/", "/1000/", "/1103/", "/1104/", "/5199/", "/5200/", "/5200.1/", "/4294972495/", "/4294972496/", "/123456789012/", "/281479271683150.0/", "/281479271683151/"
    };

    [Theory]
    [MemberData(nameof(SqlServerVectors))]
    public void FromSqlServerBytes_DecodesWhatSqlServerStores(string path, string hex, int level)
    {
        var id = HierarchyId.FromSqlServerBytes(Convert.FromHexString(hex));

        Assert.Equal(path, id.ToString());
        Assert.Equal(level, id.Level);
    }

    [Theory]
    [MemberData(nameof(SqlServerVectors))]
    public void ToSqlServerBytes_EncodesWhatSqlServerStores(string path, string hex, int level)
    {
        var id = HierarchyId.Parse(path);

        Assert.Equal(hex, Convert.ToHexString(id.ToSqlServerBytes()));
        Assert.Equal(level, id.Level);
    }

    [Fact]
    public void CompareTo_OrdersLikeSqlServer()
    {
        var shuffled = SqlServerOrder.Reverse().Select(HierarchyId.Parse).ToList();

        shuffled.Sort();

        Assert.Equal(SqlServerOrder, shuffled.Select(h => h.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1/2/")]
    [InlineData("/1/2")]
    [InlineData("//")]
    [InlineData("/a/")]
    [InlineData("/1..2/")]
    [InlineData("/.1/")]
    [InlineData("/1./")]
    [InlineData("/+1/")]
    [InlineData("/ 1/")]
    [InlineData("/281479271683152/")]
    [InlineData("/-281479271682121/")]
    [InlineData("/281479271683151.0/")] // a dotted component is stored as value + 1 (rejected live)
    public void Parse_RejectsWhatSqlServerRejects(string text)
    {
        Assert.Throws<FormatException>(() => HierarchyId.Parse(text));
        Assert.False(HierarchyId.TryParse(text, out _));
    }

    [Fact]
    public void Root_IsTheEmptyPath()
    {
        Assert.Equal("/", HierarchyId.Root.ToString());
        Assert.Equal(0, HierarchyId.Root.Level);
        Assert.Equal(HierarchyId.Root, default);
        Assert.Equal(HierarchyId.Root, HierarchyId.Parse("/"));
        Assert.Empty(default(HierarchyId).ToSqlServerBytes());
    }

    [Fact]
    public void GetAncestor_WalksUpTheLevels()
    {
        var node = HierarchyId.Parse("/1/2.3/4/");

        Assert.Equal(node, node.GetAncestor(0));
        Assert.Equal(HierarchyId.Parse("/1/2.3/"), node.GetAncestor(1));
        Assert.Equal(HierarchyId.Parse("/1/"), node.GetAncestor(2));
        Assert.Equal(HierarchyId.Root, node.GetAncestor(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => node.GetAncestor(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => node.GetAncestor(-1));
    }

    [Fact]
    public void IsDescendantOf_IncludesTheNodeItself()
    {
        var node = HierarchyId.Parse("/1/2/");

        Assert.True(node.IsDescendantOf(node));
        Assert.True(node.IsDescendantOf(HierarchyId.Parse("/1/")));
        Assert.True(node.IsDescendantOf(HierarchyId.Root));
        Assert.False(node.IsDescendantOf(HierarchyId.Parse("/1/2/3/")));
        Assert.False(node.IsDescendantOf(HierarchyId.Parse("/1.1/")));
        Assert.False(HierarchyId.Parse("/1.1/").IsDescendantOf(HierarchyId.Parse("/1/")));
    }

    [Fact]
    public void Equality_AndOperators()
    {
        var a = HierarchyId.Parse("/1/2/");
        var b = HierarchyId.Parse("/1/2/");
        var c = HierarchyId.Parse("/1/3/");

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a < c);
        Assert.True(c > a);
        Assert.True(a <= b && a >= b);
        Assert.False(a.Equals((object)"/1/2/"));
    }

    [Fact]
    public void FromSqlServerBytes_RejectsBytesThatAreNotAHierarchyId()
    {
        // 0x0F: prefix 0000 is not an OrdPath label pattern.
        Assert.Throws<FormatException>(() => HierarchyId.FromSqlServerBytes(new byte[] { 0x0F }));
        // 0x40: label "01 00" with no terminator bit before the data ends.
        Assert.Throws<FormatException>(() => HierarchyId.FromSqlServerBytes(new byte[] { 0x40 }));
    }
}
