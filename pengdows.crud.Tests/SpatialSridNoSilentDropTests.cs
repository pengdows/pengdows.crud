using System;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests
{
    // An SRID must reach the database or the write must fail. A truncated WKB or an SRID that disagrees with
    // the one already embedded must not be written silently with the wrong or no SRID.
    public class SpatialSridNoSilentDropTests
    {
        [Fact]
        public void AddSridToWkb_TruncatedWkbWithSrid_Throws()
        {
            var truncated = new byte[] { 1, 1, 0 };

            Assert.Throws<ArgumentException>(() => SpatialConverter<Geography>.AddSridToWkb(truncated, 4326));
        }

        [Fact]
        public void AddSridToWkb_WkbAlreadyCarryingADifferentSrid_Throws()
        {
            // Little-endian Point (type 1) with the SRID flag set and SRID 3857 embedded.
            var wkb = new byte[] { 1, 1, 0, 0, 0x20, 0x11, 0x0D, 0x00, 0x00 };

            Assert.Throws<ArgumentException>(() => SpatialConverter<Geography>.AddSridToWkb(wkb, 4326));
        }

        [Fact]
        public void AddSridToWkb_WkbAlreadyCarryingTheSameSrid_IsReturnedUnchanged()
        {
            var wkb = new byte[] { 1, 1, 0, 0, 0x20, 0xE6, 0x10, 0x00, 0x00 };

            var result = SpatialConverter<Geography>.AddSridToWkb(wkb, 4326);

            Assert.Equal(wkb, result);
        }
    }
}
