using System.Collections.Generic;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002, found live: Geometry was mapped only for PostgreSQL and Geography for no
/// PostgreSQL-family database, and HStore only for PostgreSQL, so on CockroachDB/YugabyteDB the value
/// object itself reached Npgsql ("Writing values of '...Geometry' is not supported for parameters
/// having no NpgsqlDbType or DataTypeName"). Every PostgreSQL-family database binds them the same way.
/// </summary>
public sealed class PostgreSqlFamilyMappingTests
{
    [Table("family_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("geom", DbType.Object)] public Geometry? Geom { get; set; }
        [Column("geog", DbType.Object)] public Geography? Geog { get; set; }
        [Column("kv", DbType.Object)] public HStore Kv { get; set; }
    }

    private static ISqlContainer Build(SupportedDatabase product) =>
        new TableGateway<Row, int>(new DatabaseContext($"Host=x;EmulatedProduct={product}", new fakeDbFactory(product)))
            .BuildCreate(new Row
            {
                Id = 1,
                Geom = Geometry.FromWellKnownText("POINT(1 2)", 3857),
                Geog = Geography.FromWellKnownText("POINT(-87.6 41.8)", 4326),
                Kv = HStore.Parse("\"a\"=>\"1\"")
            });

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void BuildCreate_Spatial_BindsEwkbBytes(SupportedDatabase product)
    {
        var sc = Build(product);

        Assert.IsType<byte[]>(sc.GetParameterValue("i1"));
        Assert.IsType<byte[]>(sc.GetParameterValue("i2"));
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void BuildCreate_HStore_BindsADictionary(SupportedDatabase product)
    {
        var value = Assert.IsAssignableFrom<IDictionary<string, string?>>(Build(product).GetParameterValue("i3"));

        Assert.Equal("1", value["a"]);
    }
}
