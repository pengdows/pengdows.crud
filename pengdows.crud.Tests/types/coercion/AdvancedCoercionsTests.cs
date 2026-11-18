using System;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.AdvancedCoercionsCoverage
{
    public class AdvancedCoercionsTests
    {
        [Fact]
        public void HierarchyId_NullValue_ReturnsFalse()
        {
            Assert.False(new HierarchyIdCoercion().TryRead(new DbValue(null), out _));
        }

        [Fact]
        public void HierarchyId_InstanceValue_PassesThrough()
        {
            var id = HierarchyId.Parse("/1/2/");

            Assert.True(new HierarchyIdCoercion().TryRead(new DbValue(id), out var value));
            Assert.Equal(id, value);
        }

        [Fact]
        public void HierarchyId_MalformedText_ReturnsFalse()
        {
            Assert.False(new HierarchyIdCoercion().TryRead(new DbValue("not a path"), out _));
        }

        [Fact]
        public void HierarchyId_UnsupportedValue_ReturnsFalse()
        {
            Assert.False(new HierarchyIdCoercion().TryRead(new DbValue(42), out _));
        }

        [Fact]
        public void MySqlSpatial_LeadingSridThenWkb_SplitsBoth()
        {
            Assert.True(MySqlSpatialFormat.TrySplit(new byte[] { 1, 0, 0, 0, 9, 8 }, out var srid, out var wkb));

            Assert.Equal(1, srid);
            Assert.Equal(new byte[] { 9, 8 }, wkb);
        }

        [Fact]
        public void MySqlSpatial_TooShortOrNotBytes_ReturnsFalse()
        {
            Assert.False(MySqlSpatialFormat.TrySplit(new byte[] { 1, 0, 0, 0 }, out var srid, out var wkb));
            Assert.Equal(0, srid);
            Assert.Empty(wkb);
            Assert.False(MySqlSpatialFormat.TrySplit("text", out _, out _));
            Assert.False(MySqlSpatialFormat.TrySplit(null, out _, out _));
        }
    }
}
