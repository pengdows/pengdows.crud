#pragma warning disable CS0618 // TypeCoercionOptions is internal plumbing here
using System;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests
{
    /// <summary>
    /// Converter resolution is one place: ConvertWithCache converts a value to a type (compiling and caching the converter per
    /// type pair), and ResolveCoercer builds the per-column delegate a mapper calls for a field.
    /// </summary>
    public class ConverterResolutionTests
    {
        [Fact]
        public void ConvertWithCacheConvertsAndReturnsTheSameValueForTheSameType()
        {
            Assert.Equal(42L, ConverterResolution.ConvertWithCache(42, typeof(long)));
            Assert.Equal("x", ConverterResolution.ConvertWithCache("x", typeof(string)));
        }

        [Fact]
        public void ConvertWithCacheRejectsANullValue()
        {
            Assert.Throws<ArgumentNullException>(() => ConverterResolution.ConvertWithCache(null!, typeof(int)));
        }

        [Fact]
        public void ResolveCoercerReturnsANullForADbNullAndConvertsOtherwise()
        {
            var coercer = ConverterResolution.ResolveCoercer(typeof(int), typeof(long), EnumParseFailureMode.Throw, null);

            Assert.Null(coercer(DBNull.Value));
            Assert.Equal(7L, coercer(7));
        }

        [Fact]
        public void ResolveCoercerReadsADateTimeAsADateTimeOffsetUnderTheCallersTimePolicy()
        {
            var options = TypeCoercionOptions.Default with { TimePolicy = TimeMappingPolicy.ForceUtcDateTime };
            var coercer = ConverterResolution.ResolveCoercer(typeof(DateTime), typeof(DateTimeOffset), EnumParseFailureMode.Throw, options);

            var result = (DateTimeOffset)coercer(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc))!;

            Assert.Equal(TimeSpan.Zero, result.Offset);
        }
    }
}
