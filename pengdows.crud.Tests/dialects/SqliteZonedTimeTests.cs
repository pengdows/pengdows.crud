#pragma warning disable CS0618 // TypeCoercionOptions is internal plumbing here
using System;
using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.dialects
{
    /// <summary>
    /// A TIME WITH TIME ZONE value keeps its offset in SQLite: it is bound as "HH:mm:ss[.fffffff]+hh:mm" text, and that text
    /// reads back as the same time of day at the same offset. A DateTimeOffset declared as an instant stays UTC.
    /// </summary>
    public class SqliteZonedTimeTests
    {
        private static readonly TypeCoercionOptions SqliteOptions =
            TypeCoercionOptions.Default with { Provider = SupportedDatabase.Sqlite };

        private static SqliteDialect Dialect() =>
            new(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger.Instance);

        [Fact]
        public void ATimeWithZoneBindsItsOffsetNotAnInstant()
        {
            var value = new DateTimeOffset(2026, 10, 5, 13, 45, 30, TimeSpan.FromHours(2));

            var parameter = Dialect().CreateDbParameter("t", DbType.Time, value);

            Assert.Equal("13:45:30+02:00", parameter.Value);
        }

        [Fact]
        public void AnInstantDeclaredAsDateTimeOffsetStillBindsAsUtc()
        {
            var value = new DateTimeOffset(2026, 10, 5, 13, 45, 30, TimeSpan.FromHours(2));

            var parameter = Dialect().CreateDbParameter("t", DbType.DateTimeOffset, value);

            Assert.Equal("2026-10-05T11:45:30.0000000Z", parameter.Value);
        }

        [Fact]
        public void TimeWithZoneTextReadsAsTheSameTimeOfDayAtTheSameOffset()
        {
            var read = (DateTimeOffset)DateTimeCoercion.CoerceDateTimeOffsetFromString("13:45:30+02:00", SqliteOptions);

            Assert.Equal(new TimeSpan(13, 45, 30), read.TimeOfDay);
            Assert.Equal(TimeSpan.FromHours(2), read.Offset);
        }

        [Fact]
        public void TimeWithZoneTextKeepsFractionalSecondsAndNegativeOffsets()
        {
            var read = (DateTimeOffset)DateTimeCoercion.CoerceDateTimeOffsetFromString("08:05:09.5-05:30", SqliteOptions);

            Assert.Equal(new TimeSpan(8, 5, 9) + TimeSpan.FromTicks(5_000_000), read.TimeOfDay);
            Assert.Equal(new TimeSpan(-5, -30, 0), read.Offset);
        }

        [Fact]
        public void ATimeWithNoOffsetIsStillNotATimestamp()
        {
            Assert.Throws<InvalidCastException>(() => DateTimeCoercion.CoerceDateTimeOffsetFromString("13:45:30", SqliteOptions));
        }

        // Only SQLite writes a time with zone as text, so only its reads take that text. Another provider's time-only text is
        // refused rather than given a date.
        [Fact]
        public void TimeWithZoneTextIsRefusedOnAProviderThatDoesNotWriteItAsText()
        {
            var options = TypeCoercionOptions.Default with { Provider = SupportedDatabase.PostgreSql };

            Assert.Throws<InvalidCastException>(() => DateTimeCoercion.CoerceDateTimeOffsetFromString("13:45:30+02:00", options));
        }
    }
}
