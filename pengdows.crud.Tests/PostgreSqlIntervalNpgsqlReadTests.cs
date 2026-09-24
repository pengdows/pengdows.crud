#region

using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

#endregion

namespace NpgsqlTypes
{
    /// <summary>
    /// Stand-in with the same full name and shape as Npgsql's NpgsqlInterval. The converter matches
    /// Npgsql types by name (pengdows.crud takes no Npgsql dependency), and this project doesn't
    /// reference Npgsql.
    /// </summary>
    public readonly struct NpgsqlInterval
    {
        public NpgsqlInterval(int months, int days, long time)
        {
            Months = months;
            Days = days;
            Time = time;
        }

        public int Months { get; }
        public int Days { get; }
        public long Time { get; }
    }
}

namespace pengdows.crud.Tests
{
    public class PostgreSqlIntervalNpgsqlReadTests
    {
        [Theory]
        [InlineData(14, 3, 14_706_000_789L)]
        [InlineData(-2, -1, -90_000_000L)]
        [InlineData(0, 0, 129_600_000_000L)]
        public void TryConvertFromProvider_NpgsqlInterval_PreservesAllComponents(int months, int days, long time)
        {
            var converter = new PostgreSqlIntervalConverter();

            var ok = converter.TryConvertFromProvider(new NpgsqlTypes.NpgsqlInterval(months, days, time),
                SupportedDatabase.PostgreSql, out var result);

            Assert.True(ok);
            Assert.Equal(new PostgreSqlInterval(months, days, time), result);
        }

        // Without Npgsql loaded the converter falls back to ISO 8601 text; it must not drop whole
        // days held in the time part or sub-millisecond precision.
        [Theory]
        [InlineData(0L, 0, 129_600_000_000L, "PT36H")]
        [InlineData(0L, 1, 1_500_250L, "P1DT1.50025S")]
        [InlineData(0L, 0, 3_723_000_001L, "PT1H2M3.000001S")]
        public void ToProviderValue_WithoutNpgsql_FormatsIsoWithoutLosingTime(long unused, int days,
            long microseconds, string expected)
        {
            _ = unused;
            var converter = new PostgreSqlIntervalConverter();

            Assert.Equal(expected, converter.ToProviderValue(new PostgreSqlInterval(0, days, microseconds),
                SupportedDatabase.PostgreSql));
        }

        // DataReaderMapper's coercer for an object-typed value goes through CoercionRegistry; it must
        // not fall through to a silent default for an NpgsqlInterval.
        [Theory]
        [InlineData(14, 3, 14_706_000_789L)]
        [InlineData(0, 10, 0L)]
        public void ObjectCoercer_NpgsqlInterval_PreservesAllComponents(int months, int days, long time)
        {
            var coercer = TypeCoercionHelper.ResolveCoercer(typeof(object), typeof(PostgreSqlInterval),
                EnumParseFailureMode.Throw);

            var result = coercer(new NpgsqlTypes.NpgsqlInterval(months, days, time));

            Assert.Equal(new PostgreSqlInterval(months, days, time), result);
        }
    }
}
