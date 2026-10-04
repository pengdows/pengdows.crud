using System.Collections.Generic;
using System.Data;
using System.Linq;
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
/// TYPE-021: Oracle SDO_GEOMETRY without an ODP.NET UDT class. Probed live on Oracle Free 23ai (full
/// image; the slim image has no Spatial): the value is written as EWKT text bound as a CLOB and built
/// by the server with SDO_GEOMETRY(wkt, srid) inside a scalar subquery (ODP.NET binds by position, so
/// the parameter appears once), and read back as EWKT through SDO_UTIL.TO_WKTGEOMETRY plus the SRID
/// from SDO_UTIL.TO_JSON (no table alias needed). Oracle stores ordinates to 15 significant digits on
/// every path (WKT and WKB alike, confirmed live).
/// </summary>
public sealed class OracleSpatialTests
{
    [Table("geo")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("g", DbType.Object)] public Geometry? Geom { get; set; }
        [Column("gg", DbType.Object)] public Geography? Geog { get; set; }
    }

    private static string Write(string marker) =>
        "(SELECT CASE WHEN x IS NULL THEN NULL ELSE SDO_GEOMETRY(SUBSTR(x, INSTR(x, ';') + 1), " +
        $"NULLIF(TO_NUMBER(SUBSTR(x, 6, INSTR(x, ';') - 6)), 0)) END FROM (SELECT TO_CLOB({marker}) x FROM DUAL))";

    private static string Read(string column, string name) =>
        $"CASE WHEN {column} IS NULL THEN NULL ELSE 'SRID=' || NVL(JSON_VALUE(SDO_UTIL.TO_JSON({column}), '$.srid'), '0') " +
        $"|| ';' || SDO_UTIL.TO_WKTGEOMETRY({column}) END AS {name}";

    private static TableGateway<Row, int> Gateway() =>
        new(new DatabaseContext("Data Source=test;EmulatedProduct=Oracle", new fakeDbFactory(SupportedDatabase.Oracle)));

    private static Row Sample(int id = 1) => new()
    {
        Id = id,
        Geom = Geometry.FromWellKnownBinary(WellKnownTextEncoder.Encode("POINT (1.5 -2.25)"), 3857),
        Geog = Geography.FromWellKnownText("POINT (-97.7430608 30.267153)", 4326)
    };

    private static System.Data.Common.DbParameter Parameter(ISqlContainer sc, string name) =>
        ((System.Collections.Generic.IDictionary<string, System.Data.Common.DbParameter>)typeof(SqlContainer)
            .GetField("_parameters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(sc)!)[name];

    private static object? Value(ISqlContainer sc, string name) => Parameter(sc, name).Value;

    [Fact]
    public void BuildCreate_SpatialColumns_ServerBuildsSdoGeometryFromEwkt()
    {
        using var sc = Gateway().BuildCreate(Sample());
        var sql = sc.Query.ToString();

        Assert.Contains(Write(":i1"), sql);
        Assert.Contains(Write(":i2"), sql);
        Assert.Equal("SRID=3857;POINT (1.5 -2.25)", Value(sc, "i1"));
        Assert.Equal("SRID=4326;POINT (-97.7430608 30.267153)", Value(sc, "i2"));
        // Text (ODP.NET: a CLOB, set by the dialect, since as VARCHAR2 a long value is ORA-01461).
        Assert.Equal(DbType.String, Parameter(sc, "i1").DbType);
    }

    [Fact]
    public async Task BuildUpdateAsync_SpatialColumns_ServerBuildsSdoGeometry()
    {
        await using var sc = await Gateway().BuildUpdateAsync(Sample(), false);
        var sql = sc.Query.ToString();

        Assert.Contains(Write(":s0"), sql);
        Assert.Contains(Write(":s1"), sql);
    }

    [Fact]
    public void BuildUpsert_SpatialColumns_ServerBuildsSdoGeometry()
    {
        using var sc = Gateway().BuildUpsert(Sample());

        Assert.Contains("SDO_GEOMETRY(SUBSTR(x", sc.Query.ToString());
    }

    [Fact]
    public void BuildBatchCreate_ArrayBoundInsert_ServerBuildsSdoGeometry()
    {
        // Oracle's batch insert binds one array parameter per column (FEAT-005); the column's
        // conversion must wrap that marker as it does a single-row insert's.
        var sc = Assert.Single(Gateway().BuildBatchCreate(new[] { Sample(1), Sample(2) }));
        var sql = sc.Query.ToString();

        Assert.Contains(Write(":i1"), sql);
        Assert.Contains(Write(":i2"), sql);
    }

    [Fact]
    public void BuildBatchUpdate_MergeSource_ServerBuildsSdoGeometry_NullsToo()
    {
        // A bare NULL in the MERGE source's UNION ALL is typed as text, which Oracle refuses for an
        // SDO_GEOMETRY target (ORA-00932, confirmed live), so a NULL goes through the conversion too.
        var second = Sample(2);
        second.Geog = null;
        var sc = Assert.Single(Gateway().BuildBatchUpdate(new[] { Sample(1), second }));
        var sql = sc.Query.ToString();

        Assert.Contains(Write(":b1"), sql);
        Assert.Contains(Write(":b2"), sql);
        Assert.Contains(Write("NULL"), sql);
    }

    [Fact]
    public void BuildCreate_NullSpatialValue_BindsNullThroughTheSameExpression()
    {
        var row = Sample();
        row.Geom = null;
        using var sc = Gateway().BuildCreate(row);

        Assert.Contains(Write(":i1"), sc.Query.ToString());
        Assert.True(Value(sc, "i1") is null or System.DBNull);
        // ODP.NET refuses an untyped (DbType.Object) NULL with ORA-50028 (confirmed live).
        Assert.Equal(DbType.String, Parameter(sc, "i1").DbType);
    }

    [Fact]
    public void BuildBaseRetrieve_ReadsSpatialColumnsAsEwkt_WithAndWithoutAlias()
    {
        var gateway = Gateway();

        var aliased = gateway.BuildBaseRetrieve("a").Query.ToString();
        var plain = gateway.BuildBaseRetrieve("").Query.ToString();

        Assert.Contains(Read("\"a\".\"g\"", "\"g\""), aliased);
        Assert.Contains(Read("\"a\".\"gg\"", "\"gg\""), aliased);
        Assert.Contains(Read("\"g\"", "\"g\""), plain);
        Assert.Contains("\"a\".\"id\"", aliased);
    }

    [Fact]
    public async Task LoadSingleAsync_EwktFromOracle_HydratesWithItsSrid()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle", factory);
        factory.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object>
            {
                ["id"] = 1, ["g"] = "SRID=3857;POINT (1.5 -2.25)", ["gg"] = "SRID=4326;POINT (-97.7430608 30.267153)"
            }
        });
        var gateway = new TableGateway<Row, int>(context);
        await using var sc = gateway.BuildBaseRetrieve("a");

        var row = await gateway.LoadSingleAsync(sc);

        Assert.Equal(3857, row!.Geom!.Srid);
        Assert.Equal("POINT (1.5 -2.25)", row.Geom.WellKnownText);
        Assert.Equal(4326, row.Geog!.Srid);
        Assert.Equal("POINT (-97.7430608 30.267153)", row.Geog.WellKnownText);
    }

    public sealed class Mapped
    {
        public Geometry? G { get; set; }
        public Geography? Gg { get; set; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DataReaderMapper_EwktText_MapsWithItsSrid(bool strict)
    {
        // Custom SQL that selects the gateway's conversion gets EWKT text; it maps like the gateway's.
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["G"] = "SRID=3857;POINT (1.5 -2.25)", ["Gg"] = "SRID=4326;POINT (1 2)" }
        });

        var row = Assert.Single(await DataReaderMapper.LoadAsync<Mapped>(reader, new MapperOptions(Strict: strict)));

        Assert.Equal(3857, row.G!.Srid);
        Assert.Equal("POINT (1.5 -2.25)", row.G.WellKnownText);
        Assert.Equal(4326, row.Gg!.Srid);
    }

    [Fact]
    public async Task LoadSingleAsync_NullFromOracle_HydratesNull()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle", factory);
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["id"] = 1, ["g"] = System.DBNull.Value, ["gg"] = System.DBNull.Value } });
        var gateway = new TableGateway<Row, int>(context);
        await using var sc = gateway.BuildBaseRetrieve("a");

        var row = await gateway.LoadSingleAsync(sc);

        Assert.Null(row!.Geom);
        Assert.Null(row.Geog);
    }
}
