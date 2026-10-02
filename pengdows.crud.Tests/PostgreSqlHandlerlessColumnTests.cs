using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002, confirmed live (PostGIS 3.5 / CockroachDB 25.1, Npgsql 9): Npgsql has no handler for
/// PostGIS geometry/geography or vector without its plugins, so GetFieldType and GetValue throw
/// ("Reading as 'System.Object' is not supported for fields having DataTypeName 'public.geometry'")
/// and such a column could not be read at all. GetBytes returns the binary wire value, which the
/// PostgreSQL-family dialects decode: EWKB for spatial, pgvector's format for vector.
/// </summary>
public sealed class PostgreSqlHandlerlessColumnTests
{
    private static readonly byte[] Ewkb3857 =
        GeometryConverter.AddSridToWkb(WellKnownTextEncoder.Encode("POINT(1 2)"), 3857);

    private static readonly byte[] Ewkb4326 =
        GeometryConverter.AddSridToWkb(WellKnownTextEncoder.Encode("POINT(-87.6 41.8)"), 4326);

    // pgvector's binary format: int16 dimensions, int16 unused, then big-endian float4 values.
    private static byte[] Vector(params float[] values)
    {
        var bytes = new byte[4 + 4 * values.Length];
        BinaryPrimitives.WriteInt16BigEndian(bytes, (short)values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(4 + 4 * i), values[i]);
        }

        return bytes;
    }

    [Table("pg_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("geom", DbType.Object)] public Geometry? Geom { get; set; }
        [Column("geog", DbType.Object)] public Geography? Geog { get; set; }
        [Column("emb", DbType.Object)] public float[]? Emb { get; set; }
    }

    private static fakeDbDataReader Reader(object geom, object geog, object emb) =>
        new(new[] { new Dictionary<string, object> { ["id"] = 1, ["geom"] = geom, ["geog"] = geog, ["emb"] = emb } })
        {
            HandlerlessColumns = new Dictionary<string, string>
            {
                ["geom"] = "public.geometry",
                ["geog"] = "public.geography",
                ["emb"] = "vector"
            }
        };

    private static (DatabaseContext Context, fakeDbConnection Exec) Context(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = product });
        var exec = new fakeDbConnection { EmulatedProduct = product };
        factory.Connections.Add(exec);
        return (new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Host=x;EmulatedProduct={product}",
            DbMode = DbMode.Standard
        }, factory), exec);
    }

    [Fact]
    public void FakeDbReader_EmulatesNpgsqlWithoutATypeHandler()
    {
        var reader = Reader(Ewkb3857, Ewkb4326, Vector(1, 2));

        Assert.True(reader.Read());
        Assert.Equal("public.geometry", reader.GetDataTypeName(1));
        Assert.Throws<InvalidCastException>(() => reader.GetFieldType(1));
        Assert.Throws<InvalidCastException>(() => reader.GetValue(1));
        Assert.False(reader.IsDBNull(1));
        var buffer = new byte[64];
        Assert.Equal(Ewkb3857.Length, reader.GetBytes(1, 0, buffer, 0, buffer.Length));
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public async Task RetrieveOneAsync_HandlerlessColumns_AreDecoded(SupportedDatabase product)
    {
        var (context, exec) = Context(product);
        await using var _ = context;
        exec.EnqueueReaderResult(Reader(Ewkb3857, Ewkb4326, Vector(1.5f, -2f, 3.25f)));

        var row = await new TableGateway<Row, int>(context).RetrieveOneAsync(1);

        Assert.Equal(3857, row!.Geom!.Srid);
        Assert.Equal(WellKnownTextEncoder.Encode("POINT(1 2)"), row.Geom.WellKnownBinary.ToArray());
        Assert.Equal(4326, row.Geog!.Srid);
        Assert.Equal(new[] { 1.5f, -2f, 3.25f }, row.Emb);
    }

    [Fact]
    public async Task RetrieveOneAsync_NullHandlerlessColumns_AreNull()
    {
        var (context, exec) = Context(SupportedDatabase.PostgreSql);
        await using var _ = context;
        exec.EnqueueReaderResult(Reader(DBNull.Value, DBNull.Value, DBNull.Value));

        var row = await new TableGateway<Row, int>(context).RetrieveOneAsync(1);

        Assert.Null(row!.Geom);
        Assert.Null(row.Geog);
        Assert.Null(row.Emb);
    }

    [Fact]
    public async Task StrictDataReaderMapper_HandlerlessColumns_AreDecoded()
    {
        var (context, exec) = Context(SupportedDatabase.PostgreSql);
        await using var _ = context;
        exec.EnqueueReaderResult(Reader(Ewkb3857, Ewkb4326, Vector(1, 2)));
        await using var sc = context.CreateSqlContainer("SELECT id, geom, geog, emb FROM pg_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<Row>(reader, new MapperOptions(Strict: true, ColumnsOnly: true)));

        Assert.Equal(3857, row.Geom!.Srid);
        Assert.Equal(4326, row.Geog!.Srid);
        Assert.Equal(new[] { 1f, 2f }, row.Emb);
    }

    [Fact]
    public async Task TrackedReader_ReportsAndReadsHandlerlessColumns()
    {
        var (context, exec) = Context(SupportedDatabase.PostgreSql);
        await using var _ = context;
        exec.EnqueueReaderResult(Reader(Ewkb3857, Ewkb4326, Vector(1, 2)));
        await using var sc = context.CreateSqlContainer("SELECT id, geom, geog, emb FROM pg_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(typeof(Geometry), reader.GetFieldType(1));
        Assert.Equal(typeof(float[]), reader.GetFieldType(3));
        Assert.Equal(3857, Assert.IsType<Geometry>(reader.GetValue(1)).Srid);
        Assert.Equal(new[] { 1f, 2f }, reader.GetValue(3));
    }

    [Fact]
    public async Task OtherHandlerlessType_StillThrowsDataMappingException()
    {
        var (context, exec) = Context(SupportedDatabase.PostgreSql);
        await using var _ = context;
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["geom"] = new byte[] { 1 }, ["geog"] = DBNull.Value, ["emb"] = DBNull.Value }
        })
        {
            HandlerlessColumns = new Dictionary<string, string> { ["geom"] = "public.some_extension_type" }
        });

        await Assert.ThrowsAsync<pengdows.crud.exceptions.DataMappingException>(async () =>
            await new TableGateway<Row, int>(context).RetrieveOneAsync(1));
    }
}

/// <summary>
/// ExtractSridFromEwkb returned its input unchanged as the "normalized" WKB, so a value built from
/// EWKB kept the SRID flag and SRID bytes in its WellKnownBinary, and MySQL/SQL Server were then sent
/// EWKB where they take WKB (TYPE-002).
/// </summary>
public sealed class EwkbNormalizationTests
{
    [Theory]
    [InlineData("POINT(1 2)", 3857)]
    [InlineData("LINESTRING(0 0, 1 1)", 4326)]
    [InlineData("POLYGON((0 0, 4 0, 4 4, 0 0))", 27700)]
    public void ExtractSridFromEwkb_StripsTheSridFlagAndBytes(string wkt, int srid)
    {
        var wkb = pengdows.crud.types.converters.WellKnownTextEncoder.Encode(wkt);
        var ewkb = pengdows.crud.types.converters.GeometryConverter.AddSridToWkb(wkb, srid);

        pengdows.crud.types.converters.GeometryConverter.ExtractSridFromEwkb(ewkb, out var actualSrid, out var normalized);

        Assert.Equal(srid, actualSrid);
        Assert.Equal(wkb, normalized);
    }

    [Fact]
    public void ExtractSridFromEwkb_PlainWkb_IsUnchanged()
    {
        var wkb = pengdows.crud.types.converters.WellKnownTextEncoder.Encode("POINT(1 2)");

        pengdows.crud.types.converters.GeometryConverter.ExtractSridFromEwkb(wkb, out var srid, out var normalized);

        Assert.Equal(0, srid);
        Assert.Equal(wkb, normalized);
    }

    [Fact]
    public void ExtractSridFromEwkb_BigEndian_StripsTheSrid()
    {
        // Big-endian EWKB POINT(1 2), SRID 4326.
        var ewkb = System.Convert.FromHexString("0020000001000010E63FF00000000000004000000000000000");
        var wkb = System.Convert.FromHexString("00000000013FF00000000000004000000000000000");

        pengdows.crud.types.converters.GeometryConverter.ExtractSridFromEwkb(ewkb, out var srid, out var normalized);

        Assert.Equal(4326, srid);
        Assert.Equal(wkb, normalized);
    }
}
