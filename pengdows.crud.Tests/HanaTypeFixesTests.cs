using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.types.valueobjects;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002, found live on SAP HANA (HANA Express 2.0, Sap.Data.Hana.Net 2.29).
/// </summary>
public sealed class HanaTypeFixesTests
{
    // DECIMAL/SMALLDECIMAL report their field type as Decimal but GetValue returns the driver's own
    // HanaDecimal, so GetFieldValue<decimal> (a cast of GetValue) throws; GetDecimal returns the
    // value. DataReaderMapper read 0 (non-strict, logged).
    private sealed class Mapped
    {
        public decimal Amount { get; set; }
        public decimal? Maybe { get; set; }
    }

    [Fact]
    public async Task DataReaderMapper_ProviderDecimal_ReadsTheValue()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SapHana);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.SapHana });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.SapHana };
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["Amount"] = 1234567890.123456m, ["Maybe"] = -0.5m }
        })
        {
            ProviderDecimalColumns = new HashSet<string> { "Amount", "Maybe" }
        });
        factory.Connections.Add(exec);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;EmulatedProduct=SapHana",
            DbMode = DbMode.Standard
        }, factory);
        await using var sc = context.CreateSqlContainer("SELECT 1");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<Mapped>(reader, new MapperOptions(Strict: true)));

        Assert.Equal(1234567890.123456m, row.Amount);
        Assert.Equal(-0.5m, row.Maybe);
    }

    // ST_GEOMETRY takes WKB as VARBINARY (WKT text is refused: "invalid hexadecimal format"); the
    // value objects weren't mapped, so templates failed to build. DRY-023, confirmed live: plain WKB
    // carries no SRID, so an ST_GEOMETRY(4326) column refused every write ("The geometry's SRID (0)
    // does not match the column's SRID (4326)") and every read came back with SRID 0. Values are sent
    // as EWKB that always carries its SRID, even 0 (ST_GeomFromEWKB refuses EWKB without the flag),
    // through ST_GeomFromEWKB(?), and read with ST_AsEWKB().
    [Table("hana_spatial")]
    public sealed class SpatialRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("geom", DbType.Object)] public Geometry? Geom { get; set; }
        [Column("geog", DbType.Object)] public Geography? Geog { get; set; }
    }

    private static DatabaseContext SapHanaContext() =>
        new("Server=x;EmulatedProduct=SapHana", new fakeDbFactory(SupportedDatabase.SapHana));

    private const string PointWkb = "0101000000000000000000F03F0000000000000040";

    [Fact]
    public void BuildCreate_Spatial_BindsEwkbThatAlwaysCarriesTheSrid()
    {
        var sc = new TableGateway<SpatialRow, int>(SapHanaContext()).BuildCreate(new SpatialRow
        {
            Id = 1,
            Geom = Geometry.FromWellKnownText("POINT(1 2)", 0),
            Geog = Geography.FromWellKnownBinary(Convert.FromHexString(PointWkb), 4326)
        });

        Assert.Equal("010100002000000000000000000000F03F0000000000000040",
            Convert.ToHexString(Assert.IsType<byte[]>(sc.GetParameterValue("i1"))));
        Assert.Equal("0101000020E6100000000000000000F03F0000000000000040",
            Convert.ToHexString(Assert.IsType<byte[]>(sc.GetParameterValue("i2"))));
        Assert.Contains("ST_GeomFromEWKB(", sc.Query.ToString());
    }

    // A value built from EWKB already flagged is sent with its own SRID, not flagged twice.
    [Fact]
    public void BuildCreate_SpatialFromEwkb_IsNotFlaggedTwice()
    {
        var sc = new TableGateway<SpatialRow, int>(SapHanaContext()).BuildCreate(new SpatialRow
        {
            Id = 1,
            Geom = Geometry.FromWellKnownBinary(Convert.FromHexString("0101000020E6100000000000000000F03F0000000000000040"), 4326)
        });

        Assert.Equal("0101000020E6100000000000000000F03F0000000000000040",
            Convert.ToHexString(Assert.IsType<byte[]>(sc.GetParameterValue("i1"))));
    }

    [Fact]
    public async Task EveryWritePath_Spatial_ReadsTheParameterThroughStGeomFromEwkb()
    {
        var gateway = new TableGateway<SpatialRow, int>(SapHanaContext());
        var row = new SpatialRow { Id = 1, Geom = Geometry.FromWellKnownText("POINT(1 2)", 4326) };

        Assert.Contains("ST_GeomFromEWKB(", (await gateway.BuildUpdateAsync(row, loadOriginal: false)).Query.ToString());
        var upsert = gateway.BuildUpsert(row).Query.ToString();
        Assert.Contains("ST_GeomFromEWKB(", upsert);
        Assert.DoesNotContain("ST_GeomFromWKB(", upsert);
        Assert.All(gateway.BuildBatchCreate(new[] { row, new SpatialRow { Id = 2 } }),
            sc => Assert.Contains("ST_GeomFromEWKB(", sc.Query.ToString()));
    }

    [Fact]
    public void BuildBaseRetrieve_Spatial_SelectsEwkb()
    {
        var sql = new TableGateway<SpatialRow, int>(SapHanaContext()).BuildBaseRetrieve("a").Query.ToString();

        Assert.Contains("\"a\".\"geom\".ST_AsEWKB() AS \"geom\"", sql);
        Assert.Contains("\"a\".\"geog\".ST_AsEWKB() AS \"geog\"", sql);
    }

    [Fact]
    public async System.Threading.Tasks.Task EveryWritePath_Spatial_Builds()
    {
        var gateway = new TableGateway<SpatialRow, int>(SapHanaContext());
        var row = new SpatialRow { Id = 1, Geom = Geometry.FromWellKnownText("POINT(1 2)", 0), Geog = null };

        Assert.NotNull(gateway.BuildUpsert(row));
        Assert.NotNull(await gateway.BuildUpdateAsync(row, loadOriginal: false));
        Assert.NotEmpty(gateway.BuildBatchCreate(new[] { row, new SpatialRow { Id = 2 } }));
        Assert.NotNull(gateway.BuildRetrieve(new[] { 1 }));
    }

    // Sap.Data.Hana's HanaParameter rejects DbType.Object ("No mapping exists from DbType Object to a
    // known HanaDbType"), so a spatial (DbType.Object) column couldn't build its templates, whose
    // parameters start with no value (confirmed live). HANA leaves that DbType unset, as Informix does.
    [Fact]
    public void CreateDbParameter_ObjectDbType_IsNotAssigned()
    {
        var parameter = SapHanaContext().Dialect.CreateDbParameter<object?>("p", DbType.Object, null);

        Assert.NotEqual(DbType.Object, parameter.DbType);
    }

    [Fact]
    public void BuildCreate_SpatialFromGeoJsonOnly_IsRefused()
    {
        var ex = Assert.ThrowsAny<Exception>(() => new TableGateway<SpatialRow, int>(SapHanaContext()).BuildCreate(
            new SpatialRow { Id = 1, Geom = Geometry.FromGeoJson("{\"type\":\"Point\",\"coordinates\":[1,2]}", 0) }));

        Assert.IsType<NotSupportedException>(ex.GetBaseException());
    }
}
