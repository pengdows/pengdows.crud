using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests
{
    // Malformed text for a converter's own type is an error with a reason. These converters used to catch everything
    // and return false, so the caller reported a generic cast failure and the parser's message was lost.
    public class ConverterMalformedTextReasonTests
    {
        [Fact]
        public void MacAddressConverter_MalformedText_ThrowsWithAReason()
        {
            var converter = new MacAddressConverter();

            var ex = Assert.ThrowsAny<Exception>(() =>
                converter.TryConvertFromProvider("zz:zz", SupportedDatabase.PostgreSql, out _));

            Assert.IsNotType<InvalidCastException>(ex);
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        }

        [Fact]
        public void IntervalDaySecondConverter_MalformedText_ThrowsWithAReason()
        {
            var converter = new IntervalDaySecondConverter();

            var ex = Assert.ThrowsAny<Exception>(() =>
                converter.TryConvertFromProvider("not an interval", SupportedDatabase.Oracle, out _));

            Assert.IsNotType<InvalidCastException>(ex);
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        }

        [Fact]
        public void PostgreSqlIntervalConverter_MalformedText_ThrowsWithAReason()
        {
            var converter = new PostgreSqlIntervalConverter();

            var ex = Assert.ThrowsAny<Exception>(() =>
                converter.TryConvertFromProvider("definitely not an interval", SupportedDatabase.PostgreSql, out _));

            Assert.IsNotType<InvalidCastException>(ex);
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        }

        // Geometry keeps WKT as text on read and encodes it on write, so the malformed text is rejected when it is
        // written, with the encoder's reason.
        [Fact]
        public void GeometryConverter_MalformedWellKnownText_ThrowsWithAReasonOnWrite()
        {
            var converter = new GeometryConverter();
            var malformed = Geometry.FromWellKnownText("POINT(", 4326);

            var ex = Assert.ThrowsAny<Exception>(() => converter.ToProviderValue(malformed, SupportedDatabase.PostgreSql));

            Assert.IsNotType<InvalidCastException>(ex);
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        }

        [Fact]
        public void PostgreSqlRangeConverter_MalformedRangeText_ThrowsWithAReason()
        {
            var converter = new PostgreSqlRangeConverter<int>();

            var ex = Assert.ThrowsAny<Exception>(() =>
                converter.TryConvertFromProvider("[banana,apple)", SupportedDatabase.PostgreSql, out _));

            Assert.IsNotType<InvalidCastException>(ex);
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        }
    }
}
