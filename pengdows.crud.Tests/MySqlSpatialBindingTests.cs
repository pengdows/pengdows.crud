using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-018: MySQL/MariaDB store a geometry in their internal format, a 4-byte little-endian SRID
/// followed by standard WKB, and return the same format on read. A Geometry/Geography is bound as
/// those bytes (encoding WKT to WKB when the value only has text), never as the value object
/// itself (which MySqlConnector rejects) or as raw WKT bytes (which MySQL rejects).
/// </summary>
public class MySqlSpatialBindingTests
{
    [Table("spatial_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("geom", DbType.Object)] public Geometry? Geom { get; set; }
        [Column("geog", DbType.Object)] public Geography? Geog { get; set; }
    }

    public static TheoryData<string, byte[]> WktCases() => new()
    {
        { "POINT(10 20)", Wkb(1, w => Point(w, 10, 20)) },
        { "LINESTRING(0 0, 1 1, 2 0)", Wkb(2, w => { w.Write(3u); Point(w, 0, 0); Point(w, 1, 1); Point(w, 2, 0); }) },
        { "POLYGON((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 1 2, 1 1))", Wkb(3, w =>
            {
                w.Write(2u);
                w.Write(4u); Point(w, 0, 0); Point(w, 4, 0); Point(w, 4, 4); Point(w, 0, 0);
                w.Write(4u); Point(w, 1, 1); Point(w, 2, 1); Point(w, 1, 2); Point(w, 1, 1);
            }) },
        { "MULTIPOINT((1 2), (3 4))", Wkb(4, w =>
            {
                w.Write(2u);
                w.Write(Wkb(1, p => Point(p, 1, 2)));
                w.Write(Wkb(1, p => Point(p, 3, 4)));
            }) },
        { "GEOMETRYCOLLECTION(POINT(1 2), LINESTRING(0 0, 1 1))", Wkb(7, w =>
            {
                w.Write(2u);
                w.Write(Wkb(1, p => Point(p, 1, 2)));
                w.Write(Wkb(2, l => { l.Write(2u); Point(l, 0, 0); Point(l, 1, 1); }));
            }) }
    };

    [Theory]
    [MemberData(nameof(WktCases))]
    public void BuildCreate_MySqlGeometryFromWkt_BindsSridPrefixedWkb(string wkt, byte[] expectedWkb)
    {
        var sc = BuildCreate(SupportedDatabase.MySql, new Row { Id = 1, Geom = Geometry.FromWellKnownText(wkt, 0) });

        Assert.Equal(Prefixed(0, expectedWkb), sc.GetParameterValue("i1"));
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    public void BuildCreate_GeographyFromWkb_BindsSridPrefixedWkb(SupportedDatabase product)
    {
        var wkb = Wkb(1, w => Point(w, -87.6298, 41.8781));
        var sc = BuildCreate(product, new Row { Id = 1, Geog = Geography.FromWellKnownBinary(wkb, 4326) });

        Assert.Equal(Prefixed(4326, wkb), sc.GetParameterValue("i2"));
    }

    [Fact]
    public async Task RetrieveOneAsync_MySqlInternalFormat_HydratesSridAndWkb()
    {
        var wkb = Wkb(1, w => Point(w, 10, 20));
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.MySql });
        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.MySql };
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["geom"] = Prefixed(3857, wkb), ["geog"] = Prefixed(4326, wkb) }
        });
        factory.Connections.Add(execConn);
        await using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, new TypeMapRegistry());

        var row = await new TableGateway<Row, int>(ctx).RetrieveOneAsync(1);

        Assert.NotNull(row?.Geom);
        Assert.Equal(3857, row!.Geom!.Srid);
        Assert.Equal(wkb, row.Geom.WellKnownBinary.ToArray());
        Assert.Equal(4326, row.Geog!.Srid);
        Assert.Equal(wkb, row.Geog.WellKnownBinary.ToArray());
    }

    private static ISqlContainer BuildCreate(SupportedDatabase product, Row row)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product));
        return new TableGateway<Row, int>(context).BuildCreate(row);
    }

    private static byte[] Prefixed(int srid, byte[] wkb)
    {
        var result = new byte[4 + wkb.Length];
        BitConverter.GetBytes(srid).CopyTo(result, 0);
        wkb.CopyTo(result, 4);
        return result;
    }

    private static byte[] Wkb(uint type, Action<BinaryWriter> body)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1); // little endian
        writer.Write(type);
        body(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static void Point(BinaryWriter writer, double x, double y)
    {
        writer.Write(x);
        writer.Write(y);
    }
}
