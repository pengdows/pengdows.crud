using System;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.Multiranges
{
    public class PostgreSqlMultirangeCoercionTests
    {
        private static readonly PostgreSqlMultirangeCoercion Coercion = new(SupportedDatabase.PostgreSql);

        [Fact]
        public void TryRead_NullValue_ReturnsFalse()
        {
            Assert.False(Coercion.TryRead(new DbValue(null), out var value));
            Assert.Null(value);
        }

        [Fact]
        public void TryRead_DriverRangeArray_PassesThrough()
        {
            var ranges = new[] { new Range<int>(1, 5), new Range<int>(7, 9) };

            Assert.True(Coercion.TryRead(new DbValue(ranges), out var value));
            Assert.Equal(ranges, value);
        }

        [Fact]
        public void TryRead_ArrayOfRangeText_ParsesEachElement()
        {
            var items = new object[] { "[1,5)", "[7,9)" };

            Assert.True(Coercion.TryRead(new DbValue(items), out var value));
            Assert.Equal(2, value!.Length);
            Assert.Equal(1, value[0].Lower);
            Assert.Equal(9, value[1].Upper);
        }

        [Fact]
        public void TryRead_ArrayWithAnUnparseableElement_ThrowsFormatException()
        {
            // A malformed element is a data error (the range converter throws), not a missing value.
            var items = new object[] { "[1,5)", "not-a-range" };

            Assert.Throws<FormatException>(() => Coercion.TryRead(new DbValue(items), out _));
        }

        [Fact]
        public void TryRead_SemicolonSeparatedText_UsesTheRangeArrayFallback()
        {
            Assert.True(Coercion.TryRead(new DbValue("[1,5);[7,9)"), out var value));
            Assert.Equal(2, value!.Length);
        }
    }
}
