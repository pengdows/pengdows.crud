using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

// Stand-ins for Npgsql's geometric types. The converter recognizes them by their "NpgsqlTypes." namespace and reads their
// PostgreSQL text through ToString(), so each one only has to report that text.
namespace NpgsqlTypes
{
    public sealed class NpgsqlPoint
    {
        public override string ToString() => "(3,4)";
    }

    public sealed class NpgsqlPolygon
    {
        public override string ToString() => "((0,0),(1,0),(1,1))";
    }
}

namespace pengdows.crud.Tests.GeometricConverters
{
public class PostgreSqlGeometricConvertersTests
{
    [Fact]
    public void Point_NativeValue_PassesThrough()
    {
        var point = new PostgreSqlPoint(1, 2);

        Assert.True(new PostgreSqlPointConverter().TryConvertFromProvider(point, SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(point, result);
    }

    [Fact]
    public void Point_Text_IsParsed()
    {
        Assert.True(new PostgreSqlPointConverter().TryConvertFromProvider("(5,6)", SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(5, result.X);
        Assert.Equal(6, result.Y);
    }

    [Fact]
    public void Point_NpgsqlStandIn_IsParsedFromItsText()
    {
        Assert.True(new PostgreSqlPointConverter().TryConvertFromProvider(new NpgsqlTypes.NpgsqlPoint(),
            SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(3, result.X);
        Assert.Equal(4, result.Y);
    }

    [Fact]
    public void Point_UnsupportedValue_ReturnsFalse()
    {
        Assert.False(new PostgreSqlPointConverter().TryConvertFromProvider(42, SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(default, result);
    }

    [Fact]
    public void Polygon_NativeValue_PassesThrough()
    {
        var polygon = PostgreSqlPolygon.TryParse("((0,0),(1,0),(1,1))", out var parsed) ? parsed : default;

        Assert.True(new PostgreSqlPolygonConverter().TryConvertFromProvider(polygon, SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(polygon, result);
    }

    [Fact]
    public void Polygon_Text_IsParsed()
    {
        Assert.True(new PostgreSqlPolygonConverter().TryConvertFromProvider("((0,0),(2,0),(2,2))",
            SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(3, result.Points.Count);
    }

    [Fact]
    public void Polygon_NpgsqlStandIn_IsParsedFromItsText()
    {
        Assert.True(new PostgreSqlPolygonConverter().TryConvertFromProvider(new NpgsqlTypes.NpgsqlPolygon(),
            SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(3, result.Points.Count);
    }

    [Fact]
    public void Polygon_UnsupportedValue_ReturnsFalse()
    {
        Assert.False(new PostgreSqlPolygonConverter().TryConvertFromProvider(42, SupportedDatabase.PostgreSql, out var result));
        Assert.Empty(result.Points);
    }
}
}
