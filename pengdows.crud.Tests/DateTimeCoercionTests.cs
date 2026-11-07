#pragma warning disable CS0618 // TypeCoercionOptions is internal plumbing here
using System;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests
{
    /// <summary>
    /// Date and time conversion is its own unit: the UTC normalisation of a DateTime, the offset of a DateTimeOffset, and the
    /// parse of timestamp text with or without an offset.
    /// </summary>
    public class DateTimeCoercionTests
    {
        [Fact]
        public void ADateTimeNormalisesToUtc()
        {
            var local = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);

            Assert.Equal(DateTimeKind.Utc, DateTimeCoercion.NormalizeDateTime(local).Kind);
        }

        [Fact]
        public void TimestampTextWithAnOffsetKeepsItsInstant()
        {
            Assert.True(DateTimeCoercion.TryParseTimestampText("2024-01-02T03:04:05+02:00", out var parsed));

            Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), parsed);
        }

        [Fact]
        public void OffsetlessTimestampTextIsParsed()
        {
            Assert.True(DateTimeCoercion.TryParseTimestampText("2024-01-02T03:04:05", out _));
        }

        [Fact]
        public void BlankTextIsNotATimestamp()
        {
            Assert.False(DateTimeCoercion.TryParseTimestampText("   ", out _));
        }

        [Fact]
        public void AStringReadsAsADateTimeInUtc()
        {
            var dt = DateTimeCoercion.CoerceDateTimeFromString("2024-01-02T03:04:05Z");

            Assert.Equal(DateTimeKind.Utc, dt.Kind);
            Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), dt);
        }

        [Fact]
        public void AFailedTimestampTextThrowsForTheCaller()
        {
            Assert.Throws<InvalidCastException>(() => DateTimeCoercion.CoerceDateTimeFromString("not-a-date"));
        }

        [Fact]
        public void ADateTimeBecomesAnOffsetInUtcWhenTheOptionsSayItIs()
        {
            var options = TypeCoercionOptions.Default with { TimePolicy = TimeMappingPolicy.ForceUtcDateTime };

            var dto = DateTimeCoercion.DateTimeOffsetFromDateTime(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), options);

            Assert.Equal(TimeSpan.Zero, dto.Offset);
        }
    }
}
