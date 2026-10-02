using System;
using System.Buffers.Binary;
using System.IO;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002: SQL Server's stored geometry/geography encoding ([MS-SSCLRT]) decodes to SRID + WKB
/// without Microsoft.SqlServer.Types. Every fixture is the CAST(... AS varbinary(max)) output of
/// SQL Server 2025 for the WKT shown (captured live 2026-10-01). Geography stores latitude before
/// longitude; WKB is longitude (x) first.
/// </summary>
public class SqlServerSpatialFormatTests
{
    public static TheoryData<string, string, int> GeometryCases() => new()
    {
        { "00000000010C000000000000F03F0000000000000040", "POINT(1 2)", 0 },
        { "110F0000010C000000000000F03F0000000000000040", "POINT(1 2)", 3857 },
        { "00000000011400000000000000000000000000000000000000000000F03F000000000000F03F", "LINESTRING(0 0, 1 1)", 0 },
        { "0000000001040300000000000000000000000000000000000000000000000000F03F000000000000F03F0000000000000040000000000000000001000000010000000001000000FFFFFFFF0000000002",
            "LINESTRING(0 0, 1 1, 2 0)", 0 },
        { "0000000001000800000000000000000000000000000000000000000000000000104000000000000000000000000000001040000000000000104000000000000000000000000000000000000000000000F03F000000000000F03F0000000000000040000000000000F03F000000000000F03F0000000000000040000000000000F03F000000000000F03F020000000200000000000400000001000000FFFFFFFF0000000003",
            "POLYGON((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 1 2, 1 1))", 0 },
        { "00000000010402000000000000000000F03F000000000000004000000000000008400000000000001040020000000100000000010100000003000000FFFFFFFF0000000004000000000000000001000000000100000001",
            "MULTIPOINT((1 2), (3 4))", 0 },
        { "0000000001040800000000000000000000000000000000000000000000000000F03F0000000000000000000000000000F03F000000000000F03F0000000000000000000000000000000000000000000014400000000000001440000000000000184000000000000014400000000000001840000000000000184000000000000014400000000000001440020000000200000000020400000003000000FFFFFFFF0000000006000000000000000003000000000100000003",
            "MULTIPOLYGON(((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))", 0 },
        { "00000000010403000000000000000000F03F000000000000004000000000000000000000000000000000000000000000F03F000000000000F03F020000000100000000010100000003000000FFFFFFFF0000000007000000000000000001000000000100000002",
            "GEOMETRYCOLLECTION(POINT(1 2), LINESTRING(0 0, 1 1))", 0 },
        { "000000000104000000000000000001000000FFFFFFFFFFFFFFFF01", "POINT EMPTY", 0 },
        { "000000000104000000000000000001000000FFFFFFFFFFFFFFFF07", "GEOMETRYCOLLECTION EMPTY", 0 },
        { "00000000010401000000000000000000F03F000000000000F03F01000000010000000003000000FFFFFFFF000000000700000000FFFFFFFF01000000000000000001",
            "GEOMETRYCOLLECTION(POINT EMPTY, POINT(1 1))", 0 },
    };

    [Theory]
    [MemberData(nameof(GeometryCases))]
    public void Decode_Geometry_ReturnsSridAndWkb(string stored, string wkt, int srid)
    {
        var (actualSrid, wkb) = SqlServerSpatialFormat.Decode(Convert.FromHexString(stored), geography: false);

        Assert.Equal(srid, actualSrid);
        Assert.Equal(Convert.ToHexString(WellKnownTextEncoder.Encode(wkt)), Convert.ToHexString(wkb));
    }

    [Fact]
    public void Decode_GeographyPoint_SwapsLatitudeLongitude()
    {
        var (srid, wkb) = SqlServerSpatialFormat.Decode(
            Convert.FromHexString("E6100000010C6666666666E644406666666666E655C0"), geography: true);

        Assert.Equal(4326, srid);
        Assert.Equal(Convert.ToHexString(WellKnownTextEncoder.Encode("POINT(-87.6 41.8)")), Convert.ToHexString(wkb));
    }

    [Fact]
    public void Decode_GeographyPolygon_SwapsLatitudeLongitude()
    {
        var (srid, wkb) = SqlServerSpatialFormat.Decode(Convert.FromHexString(
            "E6100000010404000000000000000000000000000000000000000000000000000000000000000000F03F000000000000F03F000000000000F03F0000000000000000000000000000000001000000020000000001000000FFFFFFFF0000000003"),
            geography: true);

        Assert.Equal(4326, srid);
        Assert.Equal(Convert.ToHexString(WellKnownTextEncoder.Encode("POLYGON((0 0, 1 0, 1 1, 0 0))")), Convert.ToHexString(wkb));
    }

    [Fact]
    public void Decode_PointZ_ReturnsIsoWkbWithZ()
    {
        var (_, wkb) = SqlServerSpatialFormat.Decode(
            Convert.FromHexString("00000000010D000000000000F03F00000000000000400000000000000840"), geography: false);

        Assert.Equal(Convert.ToHexString(IsoPoint(1001, 1, 2, 3)), Convert.ToHexString(wkb));
    }

    [Fact]
    public void Decode_PointZM_ReturnsIsoWkbWithZAndM()
    {
        var (_, wkb) = SqlServerSpatialFormat.Decode(
            Convert.FromHexString("00000000010F000000000000F03F000000000000004000000000000008400000000000001040"), geography: false);

        Assert.Equal(Convert.ToHexString(IsoPoint(3001, 1, 2, 3, 4)), Convert.ToHexString(wkb));
    }

    [Theory]
    [InlineData("00000000030C000000000000F03F0000000000000040")] // unknown serialization version
    [InlineData("0000000001")] // truncated
    public void Decode_UnreadableData_Throws(string stored)
    {
        Assert.ThrowsAny<Exception>(() => SqlServerSpatialFormat.Decode(Convert.FromHexString(stored), geography: false));
    }

    [Fact]
    public void ConstructorArgument_GeometryFromWkt_IsBigEndianSridThenWkb()
    {
        var arg = SqlServerSpatialFormat.ToConstructorArgument(Geometry.FromWellKnownText("POINT(1 2)", 3857));

        Assert.Equal(3857, BinaryPrimitives.ReadInt32BigEndian(arg));
        Assert.Equal(WellKnownTextEncoder.Encode("POINT(1 2)"), arg[4..]);
    }

    [Fact]
    public void ConstructorArgument_GeographyFromEwkb_DropsTheEmbeddedSrid()
    {
        var wkb = WellKnownTextEncoder.Encode("POINT(-87.6 41.8)");
        var arg = SqlServerSpatialFormat.ToConstructorArgument(Geography.FromWellKnownBinary(wkb, 4326));

        Assert.Equal(4326, BinaryPrimitives.ReadInt32BigEndian(arg));
        Assert.Equal(wkb, arg[4..]);
    }

    [Fact]
    public void ConstructorArgument_GeoJsonOnly_Throws()
    {
        var value = Geometry.FromGeoJson("{\"type\":\"Point\",\"coordinates\":[1,2]}", 0);

        Assert.Throws<NotSupportedException>(() => SqlServerSpatialFormat.ToConstructorArgument(value));
    }

    private static byte[] IsoPoint(uint type, params double[] ordinates)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(type);
        foreach (var o in ordinates)
        {
            writer.Write(o);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
