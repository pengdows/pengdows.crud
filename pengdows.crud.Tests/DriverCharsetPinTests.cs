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

namespace FirebirdSql.Data.Common
{
    // Stand-in with the shape of FirebirdSql.Data.FirebirdClient's Charset table: NONE is reached through
    // TryGetByName/DefaultCharset, its encoding chosen once from the ANSI code page (Encoding2.Default).
    internal sealed class Charset
    {
        private static readonly Charset None = new();

        public static Charset DefaultCharset => None;

        public static bool TryGetByName(string name, out Charset charset)
        {
            charset = None;
            return name == "NONE";
        }

        public Encoding Encoding { get; } = Encoding.Latin1;
    }
}

namespace pengdows.crud.Tests
{
    /// <summary>
    /// InterBaseSql.Data.InterBaseClient and FirebirdSql.Data.FirebirdClient each resolve their NONE
    /// charset once: to the system code page when .NET code pages are registered first (SqlClient
    /// registers them), else to UTF-8. With a code page, text outside it is stored and read wrongly
    /// with no error (confirmed live: SQL Server then InterBase in one process; Firebird after the other
    /// drivers in the integration run, "héllo" read as "h�llo"). Both dialects pin NONE to UTF-8
    /// (maintainer decision 2026-10-03, applied to Firebird 2026-10-05) through one DriverCharsetPin.
    /// </summary>
    public sealed class DriverCharsetPinTests
    {
        [Fact]
        public void PinNoneToUtf8_CodePageNone_BecomesUtf8_AndStaysPinned()
        {
            Assert.True(DriverCharsetPin.PinNoneToUtf8(typeof(DriverCharsetPinTests).Assembly, "InterBaseSql.Data.Common.Charset", NullLogger.Instance));

            Assert.Equal(Encoding.UTF8.WebName, InterBaseSql.Data.Common.Charset.GetCharset("NONE").Encoding.WebName);
            Assert.True(DriverCharsetPin.PinNoneToUtf8(typeof(DriverCharsetPinTests).Assembly, "InterBaseSql.Data.Common.Charset", NullLogger.Instance));
            Assert.Equal(Encoding.UTF8.WebName, InterBaseSql.Data.Common.Charset.GetCharset("NONE").Encoding.WebName);
        }

        [Fact]
        public void PinNoneToUtf8_DriverWithoutTheCharsetTable_ReportsFalseWithoutThrowing()
        {
            Assert.False(DriverCharsetPin.PinNoneToUtf8(typeof(object).Assembly, "InterBaseSql.Data.Common.Charset", NullLogger.Instance));
        }

        [Fact]
        public void PinNoneToUtf8_FirebirdShape_BecomesUtf8()
        {
            Assert.True(DriverCharsetPin.PinNoneToUtf8(typeof(DriverCharsetPinTests).Assembly, "FirebirdSql.Data.Common.Charset", NullLogger.Instance));

            Assert.Equal(Encoding.UTF8.WebName, FirebirdSql.Data.Common.Charset.DefaultCharset.Encoding.WebName);
        }
    }
}
