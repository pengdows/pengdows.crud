using System;
using System.Data;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

// Stands in for Npgsql's NpgsqlPoint, which the library reads by namespace; its text has a space after the comma.
namespace NpgsqlTypes
{
    public sealed class FakeNativePoint
    {
        public override string ToString() => "(1, 2)";
    }
}

namespace pengdows.crud.Tests
{
    /// <summary>
    /// PostgreSQL's native point and polygon are value types of their own: PostgreSqlPoint and PostgreSqlPolygon carry the
    /// coordinates (no SRID, which a native geometric column does not hold) and read and write their PostgreSQL text.
    /// </summary>
    public class PostgreSqlGeometricTypeTests
    {
        [Fact]
        public void APointParsesItsCoordinatesAndWritesThemBack()
        {
            var point = PostgreSqlPoint.Parse("(1,2)");

            Assert.Equal(1d, point.X);
            Assert.Equal(2d, point.Y);
            Assert.Equal("(1,2)", point.ToString());
        }

        [Fact]
        public void APointTolerantlyParsesSpacesAndFractionsAndNegatives()
        {
            var point = PostgreSqlPoint.Parse("( 1.5 , -2 )");

            Assert.Equal(new PostgreSqlPoint(1.5, -2), point);
        }

        [Fact]
        public void APointRefusesMalformedText()
        {
            Assert.False(PostgreSqlPoint.TryParse("1,2", out _));
            Assert.False(PostgreSqlPoint.TryParse("(1,2", out _));
            Assert.False(PostgreSqlPoint.TryParse("(a,2)", out _));
            Assert.Throws<FormatException>(() => PostgreSqlPoint.Parse("(1)"));
        }

        [Fact]
        public void APolygonParsesItsPointsInOrderAndWritesThemBack()
        {
            var polygon = PostgreSqlPolygon.Parse("((0,0),(1,0),(1,1),(0,0))");

            Assert.Equal(4, polygon.Points.Count);
            Assert.Equal(new PostgreSqlPoint(1, 1), polygon.Points[2]);
            Assert.Equal("((0,0),(1,0),(1,1),(0,0))", polygon.ToString());
        }

        [Fact]
        public void APolygonNeedsAtLeastOnePoint()
        {
            Assert.Throws<ArgumentException>(() => new PostgreSqlPolygon(Array.Empty<PostgreSqlPoint>()));
            Assert.False(PostgreSqlPolygon.TryParse("()", out _));
        }

        [Fact]
        public void APointEqualityIsByCoordinates()
        {
            Assert.Equal(new PostgreSqlPoint(3, 4), new PostgreSqlPoint(3, 4));
            Assert.NotEqual(new PostgreSqlPoint(3, 4), new PostgreSqlPoint(4, 3));
            Assert.True(new PostgreSqlPoint(3, 4) == new PostgreSqlPoint(3, 4));
        }

        [Fact]
        public void TheConverterReadsNpgsqlsOwnPointText()
        {
            var converter = new PostgreSqlPointConverter();

            Assert.True(converter.TryConvertFromProvider(new NpgsqlTypes.FakeNativePoint(), SupportedDatabase.PostgreSql, out var point));

            Assert.Equal(new PostgreSqlPoint(1, 2), point);
        }

        [Fact]
        public void TheConverterWritesThePointAsItsText()
        {
            var converter = new PostgreSqlPointConverter();

            var provider = converter.ToProviderValue(new PostgreSqlPoint(1, 2), SupportedDatabase.PostgreSql);

            Assert.Equal("(1,2)", provider);
        }

        [Fact]
        public void ADefaultPolygonIsWrittenAsNullNotAsAnEmptyPolygon()
        {
            // default(PostgreSqlPolygon) has no points; PostgreSQL refuses "()", so the unset value is NULL.
            var converter = new PostgreSqlPolygonConverter();

            Assert.Null(converter.ToProviderValue(default(PostgreSqlPolygon), SupportedDatabase.PostgreSql));
        }

        [Fact]
        public void APointColumnBindsItsTextAndIsCastToTheNativeType()
        {
            using var context = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", new fakeDbFactory(SupportedDatabase.PostgreSql));
            var gateway = new TableGateway<NativePointEntity, int>(context);

            using var sc = gateway.BuildCreate(new NativePointEntity { Id = 1, Location = new PostgreSqlPoint(1, 2) });

            Assert.Contains("CAST(CAST(", sc.Query.ToString());
            Assert.Contains(" AS point)", sc.Query.ToString());
        }
    }

    [Table("native_point")]
    public class NativePointEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("location", DbType.Object, NativeType = "point")]
        public PostgreSqlPoint Location { get; set; }
    }
}
