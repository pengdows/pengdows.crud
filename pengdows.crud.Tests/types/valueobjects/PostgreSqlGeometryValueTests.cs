using System;
using System.Collections.Generic;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.GeometryValues
{
    public class PostgreSqlGeometryValueTests
    {
        [Fact]
        public void Point_NonFiniteCoordinate_Throws()
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new PostgreSqlPoint(double.NaN, 1));
            Assert.Equal("x", ex.ParamName);
            Assert.Throws<ArgumentOutOfRangeException>(() => new PostgreSqlPoint(1, double.PositiveInfinity));
        }

        [Fact]
        public void Point_EqualCoordinates_AreEqualWithEqualHashCodes()
        {
            var a = new PostgreSqlPoint(1, 2);
            var b = new PostgreSqlPoint(1, 2);

            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.True(a == b);
            Assert.False(a != b);
        }

        [Fact]
        public void Point_DifferentCoordinates_AreNotEqual()
        {
            var a = new PostgreSqlPoint(1, 2);
            var b = new PostgreSqlPoint(2, 1);

            Assert.False(a.Equals((object)b));
            Assert.False(a.Equals("not a point"));
            Assert.True(a != b);
        }

        [Fact]
        public void Point_InvalidText_DoesNotParse()
        {
            Assert.False(PostgreSqlPoint.TryParse("(1;2)", out _));
            Assert.False(PostgreSqlPoint.TryParse(null, out _));
        }

        [Fact]
        public void Polygon_NullPoints_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PostgreSqlPolygon(null!));
        }

        [Fact]
        public void Polygon_NoPoints_Throws()
        {
            Assert.ThrowsAny<ArgumentException>(() => new PostgreSqlPolygon(new List<PostgreSqlPoint>()));
        }

        [Fact]
        public void Polygon_EqualVertices_AreEqualWithEqualHashCodes()
        {
            var a = new PostgreSqlPolygon(new[] { new PostgreSqlPoint(0, 0), new PostgreSqlPoint(1, 0), new PostgreSqlPoint(1, 1) });
            var b = new PostgreSqlPolygon(new[] { new PostgreSqlPoint(0, 0), new PostgreSqlPoint(1, 0), new PostgreSqlPoint(1, 1) });

            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.True(a == b);
            Assert.False(a != b);
        }

        [Fact]
        public void Polygon_DifferentVerticesOrNonPolygon_AreNotEqual()
        {
            var a = new PostgreSqlPolygon(new[] { new PostgreSqlPoint(0, 0) });
            var b = new PostgreSqlPolygon(new[] { new PostgreSqlPoint(9, 9) });

            Assert.False(a.Equals((object)b));
            Assert.False(a.Equals(42));
            Assert.True(a != b);
        }

        [Fact]
        public void Polygon_InvalidText_DoesNotParse()
        {
            Assert.False(PostgreSqlPolygon.TryParse("((0,0),(1,", out _));
            Assert.False(PostgreSqlPolygon.TryParse("garbage", out _));
            Assert.False(PostgreSqlPolygon.TryParse(null, out _));
        }
    }
}
