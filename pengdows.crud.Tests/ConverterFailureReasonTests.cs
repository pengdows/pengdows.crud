using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests
{
    // A value that has the right shape for a converter but is malformed is an error, and the reason must reach the
    // caller. Returning false would make it indistinguishable from "not this converter's type", and the caller would
    // then report a generic cast failure with the parser's message thrown away.
    public class ConverterFailureReasonTests
    {
        [Fact]
        public void CidrConverter_MalformedText_ThrowsTheParserReason()
        {
            var converter = new CidrConverter();

            var ex = Assert.ThrowsAny<FormatException>(() =>
                converter.TryConvertFromProvider("not-a-cidr", SupportedDatabase.PostgreSql, out _));

            Assert.Contains("network/prefix", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void CidrConverter_UnrelatedObject_StillReturnsFalse()
        {
            var converter = new CidrConverter();

            Assert.False(converter.TryConvertFromProvider(new object(), SupportedDatabase.PostgreSql, out _));
        }
    }
}
