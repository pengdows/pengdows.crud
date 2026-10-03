using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.@internal;
using Xunit;

namespace InterBaseSql.Data.Common
{
    // Stand-in with the shape of InterBaseSql.Data.InterBaseClient's Charset table: NONE's encoding
    // is chosen once in a static initializer (here the code-page result, Latin-1).
    internal sealed class Charset
    {
        private static readonly Charset None = new();

        public static Charset GetCharset(string name) => None;

        public Encoding Encoding { get; private set; } = Encoding.Latin1;
    }
}

namespace pengdows.crud.Tests
{
    /// <summary>
    /// InterBaseSql.Data.InterBaseClient resolves its NONE charset once: to the system code page when
    /// .NET code pages are registered first (SqlClient registers them), else to UTF-8. With a code page,
    /// text outside it is stored as '?' silently (confirmed live: SQL Server then InterBase in one
    /// process). InterBaseDialect pins NONE to UTF-8 (maintainer decision 2026-10-03).
    /// </summary>
    public sealed class InterBaseCharsetPinTests
    {
        [Fact]
        public void PinNoneToUtf8_CodePageNone_BecomesUtf8_AndStaysPinned()
        {
            Assert.True(InterBaseCharsetPin.PinNoneToUtf8(typeof(InterBaseCharsetPinTests).Assembly, NullLogger.Instance));

            Assert.Equal(Encoding.UTF8.WebName, InterBaseSql.Data.Common.Charset.GetCharset("NONE").Encoding.WebName);
            Assert.True(InterBaseCharsetPin.PinNoneToUtf8(typeof(InterBaseCharsetPinTests).Assembly, NullLogger.Instance));
            Assert.Equal(Encoding.UTF8.WebName, InterBaseSql.Data.Common.Charset.GetCharset("NONE").Encoding.WebName);
        }

        [Fact]
        public void PinNoneToUtf8_DriverWithoutTheCharsetTable_ReportsFalseWithoutThrowing()
        {
            Assert.False(InterBaseCharsetPin.PinNoneToUtf8(typeof(object).Assembly, NullLogger.Instance));
        }
    }
}
