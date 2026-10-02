using System;
using System.Buffers.Binary;
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
/// TYPE-002: SQL Server geometry/geography without Microsoft.SqlServer.Types (Windows-only native
/// code). Without it SqlClient's GetValue throws but GetBytes returns the stored encoding, which is
/// decoded on read. On write the gateways bind a big-endian SRID + WKB as varbinary and render the
/// column's value as geometry::STGeomFromWKB(...) / geography::STGeomFromWKB(...), so SQL Server
/// builds and validates the instance itself and the SRID is kept (confirmed live, SQL Server 2025).
/// </summary>
public sealed class SqlServerSpatialMappingTests
{
    // POINT(1 2), SRID 3857 and POINT(-87.6 41.8) as geography SRID 4326, as SQL Server stores them.
    private static readonly byte[] StoredGeometry = Convert.FromHexString("110F0000010C000000000000F03F0000000000000040");
    private static readonly byte[] StoredGeography = Convert.FromHexString("E6100000010C6666666666E644406666666666E655C0");

    [Fact]
    public async Task RetrieveOneAsync_SpatialColumns_HydrateFromTheStoredEncoding()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(SpatialReader(StoredGeometry, StoredGeography));

        var row = await new TableGateway<Row, int>(context).RetrieveOneAsync(1);

        Assert.Equal(3857, row!.Geom!.Srid);
        Assert.Equal(WellKnownTextEncoder.Encode("POINT(1 2)"), row.Geom.WellKnownBinary.ToArray());
        Assert.Equal(4326, row.Geog!.Srid);
        Assert.Equal(WellKnownTextEncoder.Encode("POINT(-87.6 41.8)"), row.Geog.WellKnownBinary.ToArray());
    }

    [Fact]
    public async Task RetrieveOneAsync_NullSpatialColumns_HydrateNull()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(SpatialReader(DBNull.Value, DBNull.Value));

        var row = await new TableGateway<Row, int>(context).RetrieveOneAsync(1);

        Assert.Null(row!.Geom);
        Assert.Null(row.Geog);
    }

    [Fact]
    public async Task TrackedReader_ReportsAndReadsSpatialColumns()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(SpatialReader(StoredGeometry, StoredGeography));
        await using var sc = context.CreateSqlContainer("SELECT id, geom, geog FROM spatial_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(typeof(Geometry), reader.GetFieldType(1));
        Assert.Equal(typeof(Geography), reader.GetFieldType(2));
        Assert.Equal(3857, Assert.IsType<Geometry>(reader.GetValue(1)).Srid);
        Assert.Equal(4326, Assert.IsType<Geography>(reader.GetValue(2)).Srid);
    }

    [Fact]
    public async Task StrictDataReaderMapper_SpatialColumns_Map()
    {
        var (context, exec) = Context();
        await using var _ = context;
        exec.EnqueueReaderResult(SpatialReader(StoredGeometry, StoredGeography));
        await using var sc = context.CreateSqlContainer("SELECT id, geom, geog FROM spatial_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<Row>(reader, new MapperOptions(Strict: true)));

        Assert.Equal(3857, row.Geom!.Srid);
        Assert.Equal(4326, row.Geog!.Srid);
    }

    [Fact]
    public void BuildCreate_SpatialColumns_ServerBuildsTheInstanceFromSridPrefixedWkb()
    {
        var sc = Gateway().BuildCreate(Sample());
        var sql = sc.Query.ToString();

        Assert.Contains(Construct("geometry", "@i1"), sql);
        Assert.Contains(Construct("geography", "@i2"), sql);
        AssertArgument(sc.GetParameterValue("i1"), 3857, "POINT(1 2)");
        AssertArgument(sc.GetParameterValue("i2"), 4326, "POINT(-87.6 41.8)");
    }

    [Fact]
    public async Task BuildUpdateAsync_SpatialColumns_ServerBuildsTheInstance()
    {
        var sc = await Gateway().BuildUpdateAsync(Sample(), loadOriginal: false);
        var sql = sc.Query.ToString();

        Assert.Contains(Construct("geometry", "@s0"), sql);
        Assert.Contains(Construct("geography", "@s1"), sql);
        AssertArgument(sc.GetParameterValue("s0"), 3857, "POINT(1 2)");
    }

    [Fact]
    public void BuildUpsert_SpatialColumns_ServerBuildsTheInstance()
    {
        var sql = Gateway().BuildUpsert(Sample()).Query.ToString();

        Assert.Contains("geometry::STGeomFromWKB(", sql);
        Assert.Contains("geography::STGeomFromWKB(", sql);
    }

    [Fact]
    public void BuildCreate_NullSpatialValue_StillRendersTheVarbinaryCast()
    {
        var row = Sample();
        row.Geom = null;

        var sql = Gateway().BuildCreate(row).Query.ToString();

        Assert.Contains(Construct("geometry", "@i1"), sql);
    }

    [Fact]
    public void BuildBatchCreate_SpatialColumns_ServerBuildsEachInstance()
    {
        var sc = Assert.Single(Gateway().BuildBatchCreate(new[] { Sample(1), Sample(2) }));
        var sql = sc.Query.ToString();

        Assert.Equal(2, Occurrences(sql, "geometry::STGeomFromWKB("));
        Assert.Equal(2, Occurrences(sql, "geography::STGeomFromWKB("));
    }

    [Fact]
    public void BuildBatchCreate_NullSpatialValue_IsInlinedAsNull()
    {
        var row = Sample();
        row.Geom = null;
        var sql = Assert.Single(Gateway().BuildBatchCreate(new[] { row })).Query.ToString();

        Assert.DoesNotContain("geometry::STGeomFromWKB(", sql);
        Assert.Contains("geography::STGeomFromWKB(", sql);
    }

    [Fact]
    public void BuildBatchUpdate_SpatialColumns_ServerBuildsEachInstance()
    {
        var sql = Assert.Single(Gateway().BuildBatchUpdate(new[] { Sample(1), Sample(2) })).Query.ToString();

        Assert.Equal(2, Occurrences(sql, "geometry::STGeomFromWKB("));
        Assert.Equal(2, Occurrences(sql, "geography::STGeomFromWKB("));
    }

    [Fact]
    public void BuildBatchUpsert_SpatialColumns_ServerBuildsEachInstance()
    {
        // SQL Server's batch upsert is one MERGE per entity.
        var containers = Gateway().BuildBatchUpsert(new[] { Sample(1), Sample(2) });

        Assert.Equal(2, containers.Count);
        Assert.All(containers, sc =>
        {
            Assert.Contains("geometry::STGeomFromWKB(", sc.Query.ToString());
            Assert.Contains("geography::STGeomFromWKB(", sc.Query.ToString());
        });
    }

    [Fact]
    public async Task PrimaryKeyGateway_EveryWritePath_ServerBuildsTheInstance()
    {
        var gateway = new PrimaryKeyTableGateway<KeyedRow>(new DatabaseContext(
            "Data Source=test;EmulatedProduct=SqlServer", new fakeDbFactory(SupportedDatabase.SqlServer)));
        var row = new KeyedRow { Code = "a", Geom = Geometry.FromWellKnownText("POINT(1 2)", 3857) };

        Assert.Contains("geometry::STGeomFromWKB(", gateway.BuildCreate(row).Query.ToString());
        Assert.Contains("geometry::STGeomFromWKB(", (await gateway.BuildUpdateAsync(row)).Query.ToString());
        Assert.Contains("geometry::STGeomFromWKB(", gateway.BuildUpsert(row).Query.ToString());
        Assert.Contains("geometry::STGeomFromWKB(", Assert.Single(gateway.BuildBatchCreate(new[] { row })).Query.ToString());
        Assert.Contains("geometry::STGeomFromWKB(", Assert.Single(gateway.BuildBatchUpdate(new[] { row })).Query.ToString());
        Assert.Contains("geometry::STGeomFromWKB(", Assert.Single(gateway.BuildBatchUpsert(new[] { row })).Query.ToString());
    }

    [Fact]
    public void BuildCreate_OtherDatabase_LeavesSpatialPlaceholdersAlone()
    {
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql",
            new fakeDbFactory(SupportedDatabase.PostgreSql));

        var sql = new TableGateway<Row, int>(context).BuildCreate(Sample()).Query.ToString();

        Assert.DoesNotContain("STGeomFromWKB", sql);
    }

    // A NULL spatial value binds as DbType.Object, which SqlClient sends as sql_variant: SUBSTRING
    // rejects a sql_variant and STGeomFromWKB a NULL argument (both confirmed live), hence the cast
    // and the CASE.
    private static string Construct(string type, string marker) =>
        $"CASE WHEN {marker} IS NULL THEN NULL ELSE " +
        $"{type}::STGeomFromWKB(SUBSTRING(CAST({marker} AS varbinary(max)), 5, DATALENGTH({marker})), " +
        $"CAST(SUBSTRING(CAST({marker} AS varbinary(max)), 1, 4) AS int)) END";

    private static void AssertArgument(object? value, int srid, string wkt)
    {
        var bytes = Assert.IsType<byte[]>(value);
        Assert.Equal(srid, BinaryPrimitives.ReadInt32BigEndian(bytes));
        Assert.Equal(WellKnownTextEncoder.Encode(wkt), bytes[4..]);
    }

    private static int Occurrences(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty).Length) / value.Length;

    private static TableGateway<Row, int> Gateway() =>
        new(new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", new fakeDbFactory(SupportedDatabase.SqlServer)));

    private static Row Sample(int id = 1) => new()
    {
        Id = id,
        Geom = Geometry.FromWellKnownText("POINT(1 2)", 3857),
        Geog = Geography.FromWellKnownBinary(WellKnownTextEncoder.Encode("POINT(-87.6 41.8)"), 4326)
    };

    private static fakeDbDataReader SpatialReader(object geom, object geog) =>
        new(new[] { new Dictionary<string, object> { ["id"] = 1, ["geom"] = geom, ["geog"] = geog } })
        {
            UnloadableUdtColumns = new Dictionary<string, string>
            {
                ["geom"] = "master.sys.geometry",
                ["geog"] = "master.sys.geography"
            }
        };

    private static (DatabaseContext Context, fakeDbConnection Exec) Context()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.SqlServer });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.SqlServer };
        factory.Connections.Add(exec);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;Database=y;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard
        }, factory);
        return (context, exec);
    }

    [Table("spatial_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("geom", DbType.Object)] public Geometry? Geom { get; set; }
        [Column("geog", DbType.Object)] public Geography? Geog { get; set; }
    }

    [Table("keyed_spatial")]
    public sealed class KeyedRow
    {
        [PrimaryKey(1)] [Column("code", DbType.String)] public string Code { get; set; } = "";
        [Column("geom", DbType.Object)] public Geometry? Geom { get; set; }
    }
}
