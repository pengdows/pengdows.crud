using System;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests.Charsets
{
    public class TestCharset
    {
        public Encoding Encoding { get; set; } = Encoding.Latin1;

        public static TestCharset DefaultCharset { get; } = new();
    }

    public class ThrowingCharsetTable
    {
        public static object GetCharset(string name) => throw new InvalidOperationException("driver table changed");
    }

    public class DriverCharsetPinTests
    {
        private static readonly Assembly ThisDriver = typeof(DriverCharsetPinTests).Assembly;

        [Fact]
        public void PinNoneToUtf8_DefaultCharsetLookup_SetsTheEncodingToUtf8()
        {
            var charset = TestCharset.DefaultCharset;
            charset.Encoding = Encoding.Latin1;

            Assert.True(DriverCharsetPin.PinNoneToUtf8(ThisDriver, typeof(TestCharset).FullName!, NullLogger.Instance));

            Assert.Equal(Encoding.UTF8.CodePage, charset.Encoding.CodePage);
        }

        [Fact]
        public void PinNoneToUtf8_AlreadyUtf8_LeavesTheEncodingAndReturnsTrue()
        {
            var charset = TestCharset.DefaultCharset;
            charset.Encoding = Encoding.UTF8;

            Assert.True(DriverCharsetPin.PinNoneToUtf8(ThisDriver, typeof(TestCharset).FullName!, NullLogger.Instance));

            Assert.Equal(Encoding.UTF8.CodePage, charset.Encoding.CodePage);
        }

        [Fact]
        public void PinNoneToUtf8_LookupThrows_ReturnsFalseAndDoesNotThrow()
        {
            Assert.False(DriverCharsetPin.PinNoneToUtf8(ThisDriver, typeof(ThrowingCharsetTable).FullName!, NullLogger.Instance));
        }

        [Fact]
        public void PinNoneToUtf8_UnknownCharsetType_ReturnsFalse()
        {
            Assert.False(DriverCharsetPin.PinNoneToUtf8(ThisDriver, "No.Such.Charsets", NullLogger.Instance));
        }
    }
}
