using System.Data;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// SQLite has no spatial type. A geometry is stored as its EWKB bytes in a BLOB, so the SRID survives the trip; the
/// geometry converter reads the EWKB back with its SRID.
/// </summary>
public class SqliteSpatialTests
{
    [Fact]
    public void AGeometryIsStoredAsEwkbWithItsSrid()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.Sqlite,
            new fakeDbFactory(SupportedDatabase.Sqlite), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var point = Geometry.FromWellKnownText("POINT (1 2)", 4326);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, point);

        Assert.Equal(DbType.Binary, parameter.DbType);
        var bytes = Assert.IsType<byte[]>(parameter.Value);
        Assert.True(new GeometryConverter().TryConvertFromProvider(bytes, SupportedDatabase.Sqlite, out var back));
        Assert.Equal(4326, back.Srid);
        Assert.Equal("POINT (1 2)", ExtendedWellKnownText.WellKnownTextOf(back));
    }

    [Fact]
    public void AGeographyIsStoredAsEwkbToo()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.Sqlite,
            new fakeDbFactory(SupportedDatabase.Sqlite), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var parameter = dialect.CreateDbParameter("p", DbType.Object,
            Geography.FromWellKnownText("LINESTRING (0 0, 1 1)", 4326));

        Assert.Equal(DbType.Binary, parameter.DbType);
        Assert.IsType<byte[]>(parameter.Value);
    }
}
