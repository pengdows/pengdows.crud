using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002, found live on SingleStore 8.x (MySqlConnector): Geometry/Geography reached the driver as
/// value objects ("Parameter type Geography is not supported") because spatial was mapped only for
/// MySQL/MariaDB, and SingleStore's GEOGRAPHY takes WKT text, not MySQL's internal format; a float[]
/// was refused too, while SingleStore's VECTOR takes a JSON array as text.
/// </summary>
public sealed class SingleStoreTypeBindingTests
{
    [Table("ss_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("geog", DbType.Object)] public Geography? Geog { get; set; }
        [Column("geom", DbType.Object)] public Geometry? Geom { get; set; }
        [Column("emb", DbType.Object)] public float[]? Emb { get; set; }
    }

    private static ISqlContainer Build(Row row) =>
        new TableGateway<Row, int>(new DatabaseContext("Server=x;EmulatedProduct=SingleStore",
            new fakeDbFactory(SupportedDatabase.SingleStore))).BuildCreate(row);

    [Fact]
    public void BuildCreate_SpatialFromWkt_BindsTheText()
    {
        var sc = Build(new Row
        {
            Id = 1,
            Geog = Geography.FromWellKnownText("POINT(-87.6298 41.8781)", 4326),
            Geom = Geometry.FromWellKnownText("POLYGON((0 0, 4 0, 4 4, 0 0))", 0)
        });

        Assert.Equal("POINT(-87.6298 41.8781)", sc.GetParameterValue("i1"));
        Assert.Equal("POLYGON((0 0, 4 0, 4 4, 0 0))", sc.GetParameterValue("i2"));
    }

    // A value built from WKB (read from another database) is written as its WKT, decoded as Oracle's
    // EWKT is (WellKnownBinaryDecoder); it was refused.
    [Fact]
    public void BuildCreate_SpatialFromWkbOnly_WritesItsWkt()
    {
        var wkb = pengdows.crud.types.converters.WellKnownTextEncoder.Encode("POINT(1 2)");

        using var sc = Build(new Row { Id = 1, Geog = Geography.FromWellKnownBinary(wkb, 4326) });

        Assert.Equal("POINT (1 2)", sc.GetParameterValue("i1"));
    }

    // SingleStore returns a VECTOR(n) (F32) as its packed little-endian float32 bytes (confirmed live).
    private static readonly byte[] Packed = { 0, 0, 192, 63, 0, 0, 0, 64, 0, 0, 64, 192 };

    private static (DatabaseContext Context, fakeDbConnection Exec) VectorContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SingleStore);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.SingleStore });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.SingleStore };
        exec.EnqueueReaderResult(new[]
        {
            new System.Collections.Generic.Dictionary<string, object?> { ["id"] = 1, ["geog"] = null, ["geom"] = null, ["emb"] = Packed }
        });
        factory.Connections.Add(exec);
        return (new DatabaseContext(new pengdows.crud.configuration.DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;EmulatedProduct=SingleStore",
            DbMode = DbMode.Standard
        }, factory), exec);
    }

    [Fact]
    public async System.Threading.Tasks.Task RetrieveOneAsync_VectorBytes_ReadAsFloatArray()
    {
        var (context, _) = VectorContext();
        await using var __ = context;

        var row = await new TableGateway<Row, int>(context).RetrieveOneAsync(1);

        Assert.Equal(new[] { 1.5f, 2f, -3f }, row!.Emb);
    }

    [Fact]
    public async System.Threading.Tasks.Task DataReaderMapper_VectorBytes_ReadAsFloatArray()
    {
        var (context, _) = VectorContext();
        await using var __ = context;
        await using var sc = context.CreateSqlContainer("SELECT id, geog, geom, emb FROM ss_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        var mapped = Assert.Single(await DataReaderMapper.LoadAsync<Row>(reader, new MapperOptions(Strict: true, ColumnsOnly: true)));

        Assert.Equal(new[] { 1.5f, 2f, -3f }, mapped.Emb);
    }

    [Fact]
    public void BuildCreate_FloatArray_BindsJsonArrayText()
    {
        var sc = Build(new Row { Id = 1, Emb = new[] { 1.5f, 2f, -3f } });

        Assert.Equal("[1.5,2,-3]", sc.GetParameterValue("i3"));
    }
}
