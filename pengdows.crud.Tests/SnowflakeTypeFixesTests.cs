using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002, found live on Snowflake (Snowflake.Data 5.6): TIMESTAMP_LTZ/TIMESTAMP_TZ report their
/// field type as DateTime while GetValue returns the exact DateTimeOffset; GetDateTime returns the
/// client's local wall time (LTZ, 5 hours off here) or throws (TZ). A DateTimeOffset property reads
/// the value itself.
/// </summary>
public sealed class SnowflakeTypeFixesTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 1, 8, 45, 30, TimeSpan.FromHours(-5));

    [Table("sf_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("ltz", DbType.DateTimeOffset)] public DateTimeOffset Ltz { get; set; }
        [Column("tz", DbType.DateTimeOffset)] public DateTimeOffset? Tz { get; set; }
    }

    // VARIANT/OBJECT/ARRAY: Snowflake.Data can't bind them ("Snowflake type VARIANT is not supported
    // for parameters") and Snowflake refuses PARSE_JSON(:p) inside INSERT ... VALUES, so a JSON column is
    // written as PARSE_JSON(:p) and the statement's values come from a SELECT (all confirmed live).
    [Table("json_rows")]
    public sealed class JsonRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("doc", DbType.Object)] public pengdows.crud.types.valueobjects.JsonValue Doc { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "n";
    }

    [Table("keyed_json")]
    public sealed class KeyedJsonRow
    {
        [PrimaryKey(1)] [Column("code", DbType.String)] public string Code { get; set; } = "c";
        [Column("doc", DbType.Object)] public pengdows.crud.types.valueobjects.JsonValue Doc { get; set; }
    }

    private static DatabaseContext SnowflakeContext() =>
        new("account=x;user=y;EmulatedProduct=Snowflake", new fakeDbFactory(SupportedDatabase.Snowflake));

    private static JsonRow JsonSample(int id = 1) => new() { Id = id, Doc = new pengdows.crud.types.valueobjects.JsonValue("{\"a\":1}") };

    [Fact]
    public void BuildCreate_JsonColumn_InsertsFromSelectWithParseJson()
    {
        var sql = new TableGateway<JsonRow, int>(SnowflakeContext()).BuildCreate(JsonSample()).Query.ToString();

        Assert.Contains("PARSE_JSON(:i1)", sql);
        Assert.Contains(") SELECT ", sql);
        Assert.DoesNotContain("VALUES", sql);
    }

    [Fact]
    public async Task BuildUpdateAsync_JsonColumn_SetsParseJson()
    {
        var sql = (await new TableGateway<JsonRow, int>(SnowflakeContext()).BuildUpdateAsync(JsonSample(), loadOriginal: false)).Query.ToString();

        Assert.Contains("PARSE_JSON(:s0)", sql);
    }

    [Fact]
    public void BuildUpsert_JsonColumn_MergesFromSelect()
    {
        var sql = new TableGateway<JsonRow, int>(SnowflakeContext()).BuildUpsert(JsonSample()).Query.ToString();

        Assert.Contains("USING (SELECT ", sql);
        Assert.Contains("PARSE_JSON(", sql);
        Assert.DoesNotContain("USING (VALUES", sql);
    }

    [Fact]
    public void BuildBatchCreate_JsonColumn_InsertsFromUnionAllSelect()
    {
        var sql = Assert.Single(new TableGateway<JsonRow, int>(SnowflakeContext()).BuildBatchCreate(new[] { JsonSample(1), JsonSample(2) })).Query.ToString();

        Assert.Contains(" UNION ALL SELECT ", sql);
        Assert.Equal(2, (sql.Length - sql.Replace("PARSE_JSON(", "").Length) / "PARSE_JSON(".Length);
        Assert.DoesNotContain("VALUES", sql);
    }

    [Fact]
    public void BuildBatchUpdate_JsonColumn_UpdatesFromUnionAllSelect()
    {
        var sql = Assert.Single(new TableGateway<JsonRow, int>(SnowflakeContext()).BuildBatchUpdate(new[] { JsonSample(1), JsonSample(2) })).Query.ToString();

        Assert.Contains("PARSE_JSON(", sql);
        Assert.DoesNotContain("(VALUES", sql);
    }

    [Fact]
    public async Task PrimaryKeyGateway_JsonColumn_EveryWritePathAvoidsExpressionsInValues()
    {
        var gateway = new PrimaryKeyTableGateway<KeyedJsonRow>(SnowflakeContext());
        var row = new KeyedJsonRow { Code = "a", Doc = new pengdows.crud.types.valueobjects.JsonValue("{\"a\":1}") };

        foreach (var sql in new[]
                 {
                     gateway.BuildCreate(row).Query.ToString(),
                     (await gateway.BuildUpdateAsync(row)).Query.ToString(),
                     gateway.BuildUpsert(row).Query.ToString(),
                     Assert.Single(gateway.BuildBatchCreate(new[] { row })).Query.ToString(),
                 })
        {
            Assert.Contains("PARSE_JSON(", sql);
            Assert.DoesNotContain("VALUES (PARSE_JSON", sql);
            Assert.DoesNotContain(", PARSE_JSON(:", sql.Contains("VALUES (") ? sql[sql.IndexOf("VALUES (", StringComparison.Ordinal)..] : string.Empty);
        }
    }

    // GEOGRAPHY/GEOMETRY: Snowflake.Data can't bind the value objects ("No corresponding Snowflake type
    // for type Object"); Snowflake parses WKT, EWKT, (E)WKB hex and GeoJSON text into either column,
    // and the session returns them as EWKT so the SRID survives the read.
    [Table("spatial_rows")]
    public sealed class SpatialRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("geog", DbType.Object)] public pengdows.crud.types.valueobjects.Geography? Geog { get; set; }
        [Column("geom", DbType.Object)] public pengdows.crud.types.valueobjects.Geometry? Geom { get; set; }
    }

    [Fact]
    public void BuildCreate_Spatial_BindsEwktText()
    {
        var sc = new TableGateway<SpatialRow, int>(SnowflakeContext()).BuildCreate(new SpatialRow
        {
            Id = 1,
            Geog = pengdows.crud.types.valueobjects.Geography.FromWellKnownText("POINT(-87.6298 41.8781)", 4326),
            Geom = pengdows.crud.types.valueobjects.Geometry.FromWellKnownText("POLYGON((0 0, 4 0, 4 4, 0 0))", 0)
        });

        Assert.Equal("SRID=4326;POINT(-87.6298 41.8781)", sc.GetParameterValue("i1"));
        Assert.Equal("SRID=0;POLYGON((0 0, 4 0, 4 4, 0 0))", sc.GetParameterValue("i2"));
    }

    [Fact]
    public void BuildCreate_SpatialFromWkbOnly_BindsEwkbHex()
    {
        var wkb = pengdows.crud.types.converters.WellKnownTextEncoder.Encode("POINT(1 2)");
        var sc = new TableGateway<SpatialRow, int>(SnowflakeContext()).BuildCreate(new SpatialRow
        {
            Id = 1,
            Geog = pengdows.crud.types.valueobjects.Geography.FromWellKnownBinary(wkb, 4326)
        });

        var hex = Assert.IsType<string>(sc.GetParameterValue("i1"));
        Assert.Equal(Convert.ToHexString(pengdows.crud.types.converters.SpatialConverter<pengdows.crud.types.valueobjects.Geography>.AddSridToWkb(wkb, 4326)), hex);
    }

    [Fact]
    public void SessionSettings_ReturnSpatialAsEwkt()
    {
        var settings = ((pengdows.crud.dialects.SqlDialect)SnowflakeContext().Dialect).GetBaseSessionSettings();

        Assert.Contains("GEOGRAPHY_OUTPUT_FORMAT = 'EWKT'", settings);
        Assert.Contains("GEOMETRY_OUTPUT_FORMAT = 'EWKT'", settings);
    }

    [Fact]
    public void BuildCreate_NoJsonColumn_KeepsValues()
    {
        var sql = new TableGateway<Row, int>(SnowflakeContext()).BuildCreate(new Row { Id = 1 }).Query.ToString();

        Assert.Contains(" VALUES (", sql);
    }

    private static (DatabaseContext Context, fakeDbConnection Exec) Context()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Snowflake });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Snowflake };
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["ltz"] = Instant, ["tz"] = Instant }
        })
        {
            DateTimeOffsetReportedAsDateTimeColumns = new HashSet<string> { "ltz", "tz" }
        });
        factory.Connections.Add(exec);
        return (new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "account=x;user=y;EmulatedProduct=Snowflake",
            DbMode = DbMode.Standard
        }, factory), exec);
    }

    [Fact]
    public async Task RetrieveOneAsync_OffsetReportedAsDateTime_KeepsTheInstant()
    {
        var (context, _) = Context();
        await using var __ = context;

        var row = await new TableGateway<Row, int>(context).RetrieveOneAsync(1);

        Assert.Equal(Instant.UtcTicks, row!.Ltz.UtcTicks);
        Assert.Equal(Instant.UtcTicks, row.Tz!.Value.UtcTicks);
    }

    [Fact]
    public async Task DataReaderMapper_OffsetReportedAsDateTime_KeepsTheInstant()
    {
        var (context, _) = Context();
        await using var __ = context;
        await using var sc = context.CreateSqlContainer("SELECT id, ltz, tz FROM sf_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<Row>(reader, new MapperOptions(Strict: true, ColumnsOnly: true)));

        Assert.Equal(Instant.UtcTicks, row.Ltz.UtcTicks);
        Assert.Equal(Instant.UtcTicks, row.Tz!.Value.UtcTicks);
    }
}
