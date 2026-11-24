using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.dialects;

// DuckDB's GEOMETRY (the spatial extension) is read through ST_AsText and written through CAST(? AS GEOMETRY), because DuckDB.NET
// cannot read the type itself. The geometry has no SRID, so it travels as plain WKT.
public class DuckDbSpatialTests
{
    [Table("zz_duck_geo")]
    private sealed class Row
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("g", DbType.Object)]
        public Geometry? G { get; set; }
    }

    private static (DatabaseContext Context, DuckDbDialect Dialect, pengdows.crud.IColumnInfo Geo, pengdows.crud.IColumnInfo Id) Setup()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.DuckDB });
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=x;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard
        }, factory);
        var columns = pengdows.crud.@internal.DatabaseContextTypeMapExtensions.GetInternalTypeMapRegistry(context)
            .GetTableInfo<Row>().Columns;
        return (context, (DuckDbDialect)context.Dialect, columns["g"], columns["id"]);
    }

    [Fact]
    public void Geometry_IsWrittenThroughGeomFromText()
    {
        var (context, dialect, geo, _) = Setup();
        using var _c = context;

        Assert.True(dialect.RendersColumnArgument(geo));
        Assert.Equal("CAST(? AS GEOMETRY)", dialect.RenderColumnArgument("?", geo));
    }

    [Fact]
    public void Geometry_IsReadAsItsWellKnownText()
    {
        var (context, dialect, geo, _) = Setup();
        using var _c = context;

        Assert.Equal("ST_AsText(\"g\") AS \"g\"", dialect.RenderColumnSelect("\"g\"", "\"g\"", geo));
    }

    [Fact]
    public void NonSpatialColumn_IsUnchanged()
    {
        var (context, dialect, _, id) = Setup();
        using var _c = context;

        Assert.False(dialect.RendersColumnArgument(id));
        Assert.Equal("\"id\"", dialect.RenderColumnSelect("\"id\"", "\"id\"", id));
    }

    [Fact]
    public void Geometry_TravelsAsWellKnownTextWithoutAnSrid()
    {
        Assert.Equal(SpatialWireFormat.WellKnownTextWithoutSrid, DatabaseTraits.For(SupportedDatabase.DuckDB).SpatialFormat);
    }

    [Fact]
    public void Geometry_WithoutAnSrid_IsWrittenAsItsWellKnownText()
    {
        var written = new GeometryConverter().ToProviderValue(Geometry.FromWellKnownText("POINT (1 2)", 0), SupportedDatabase.DuckDB);

        Assert.Equal("POINT (1 2)", written);
    }

    // DuckDB's GEOMETRY has no SRID: a value that carries one is refused rather than written with the SRID silently dropped.
    [Fact]
    public void Geometry_WithAnSrid_IsRefusedRatherThanLosingTheSrid()
    {
        Assert.Throws<NotSupportedException>(() =>
            new GeometryConverter().ToProviderValue(Geometry.FromWellKnownText("POINT (1 2)", 4326), SupportedDatabase.DuckDB));
    }
}
