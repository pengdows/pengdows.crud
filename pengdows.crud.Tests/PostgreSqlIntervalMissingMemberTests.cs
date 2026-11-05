using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using Xunit;

namespace pengdows.crud.Tests
{
    // A provider object that looks like NpgsqlTimeSpan but lacks the Months member must not be read as
    // zero months. Silent zeros are data corruption; a rejected conversion is loud.
    public class PostgreSqlIntervalMissingMemberTests
    {
        [Fact]
        public void TryConvertFromProvider_NpgsqlTimeSpanWithoutMonths_ThrowsNamingTheMemberNotZeroFilled()
        {
            var converter = new PostgreSqlIntervalConverter();
            var renamed = new ProviderShape.NpgsqlTimeSpan { MonthCount = 5, Days = 2, Ticks = 30_000_000 };

            // The conversion fails and names the missing member; it never produces a value with Months = 0.
            var ex = Assert.Throws<InvalidOperationException>(() =>
                converter.TryConvertFromProvider(renamed, SupportedDatabase.PostgreSql, out _));

            Assert.Contains("'Months'", ex.Message, StringComparison.Ordinal);
        }
    }
}

// Same FullName suffix as NpgsqlTypes.NpgsqlTimeSpan, but with the member renamed.
namespace ProviderShape
{
    public sealed class NpgsqlTimeSpan
    {
        public int MonthCount { get; set; }
        public int Days { get; set; }
        public long Ticks { get; set; }
    }
}
