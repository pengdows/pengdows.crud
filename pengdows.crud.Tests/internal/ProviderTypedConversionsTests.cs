using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Net;
using pengdows.crud.@internal;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.@internal
{
    public class ProviderTypedConversionsTests
    {
        private static readonly ParameterExpression Record = Expression.Parameter(typeof(IDataRecord), "record");

        private static NpgsqlTypes.NpgsqlRange<T> Range<T>(T? lower, T? upper, bool lowerInclusive, bool upperInclusive,
            bool empty = false) where T : struct =>
            new()
            {
                LowerBound = lower.GetValueOrDefault(),
                UpperBound = upper.GetValueOrDefault(),
                LowerBoundIsInclusive = lowerInclusive,
                UpperBoundIsInclusive = upperInclusive,
                LowerBoundInfinite = lower is null,
                UpperBoundInfinite = upper is null,
                IsEmpty = empty,
            };

        // Compiles the expression TryBuild returns and runs it against a reader holding one row, column 0 = value.
        private static object? Run(Expression? expression, object? value)
        {
            Assert.NotNull(expression);
            var reader = new fakeDbDataReader(new List<Dictionary<string, object>>
            {
                new() { ["v"] = value! }
            });
            Assert.True(reader.Read());
            var body = Expression.Convert(expression!, typeof(object));
            var lambda = Expression.Lambda<Func<IDataRecord, object?>>(body, Record).Compile();
            return lambda(reader);
        }

        [Fact]
        public void Range_WithBothBounds_BuildsPortableRange()
        {
            var provider = Range<int>(1, 5, lowerInclusive: true, upperInclusive: false);

            var expression = ProviderTypedConversions.TryBuild(typeof(NpgsqlTypes.NpgsqlRange<int>),
                typeof(Range<int>), Record, 0);

            var result = Assert.IsType<Range<int>>(Run(expression, provider));
            Assert.Equal(1, result.Lower);
            Assert.Equal(5, result.Upper);
            Assert.True(result.IsLowerInclusive);
            Assert.False(result.IsUpperInclusive);
        }

        [Fact]
        public void Range_InfiniteLowerBound_MapsToNullLower()
        {
            var provider = Range<int>(null, 9, lowerInclusive: false, upperInclusive: true);

            var expression = ProviderTypedConversions.TryBuild(typeof(NpgsqlTypes.NpgsqlRange<int>),
                typeof(Range<int>), Record, 0);

            var result = Assert.IsType<Range<int>>(Run(expression, provider));
            Assert.Null(result.Lower);
            Assert.Equal(9, result.Upper);
            Assert.True(result.IsUpperInclusive);
        }

        [Fact]
        public void Range_InfiniteUpperBound_MapsToNullUpper()
        {
            var provider = Range<int>(2, null, lowerInclusive: true, upperInclusive: false);

            var expression = ProviderTypedConversions.TryBuild(typeof(NpgsqlTypes.NpgsqlRange<int>),
                typeof(Range<int>), Record, 0);

            var result = Assert.IsType<Range<int>>(Run(expression, provider));
            Assert.Equal(2, result.Lower);
            Assert.Null(result.Upper);
        }

        [Fact]
        public void Range_EmptyValue_MapsToTheEmptyRange()
        {
            var provider = Range<int>(null, null, lowerInclusive: false, upperInclusive: false, empty: true);

            var expression = ProviderTypedConversions.TryBuild(typeof(NpgsqlTypes.NpgsqlRange<int>),
                typeof(Range<int>), Record, 0);

            var result = Assert.IsType<Range<int>>(Run(expression, provider));
            Assert.True(result.IsEmpty);
        }

        [Fact]
        public void Range_NullableTarget_IsUnwrapped()
        {
            var provider = Range<int>(3, 4, lowerInclusive: true, upperInclusive: true);

            var expression = ProviderTypedConversions.TryBuild(typeof(NpgsqlTypes.NpgsqlRange<int>),
                typeof(Range<int>?), Record, 0);

            var result = Assert.IsType<Range<int>>(Run(expression, provider));
            Assert.Equal(3, result.Lower);
        }

        [Fact]
        public void Range_ElementNeedsCoercion_AppliesTheRegisteredCoercion()
        {
            // string -> DateTime has a registered typed coercion (TypedCoercions), so each bound passes through it.
            var provider = new NpgsqlTypes.NpgsqlRange<string>
            {
                LowerBound = "2024-01-02T00:00:00Z",
                UpperBound = "2024-02-01T00:00:00Z",
                LowerBoundIsInclusive = true,
            };

            var expression = ProviderTypedConversions.TryBuild(typeof(NpgsqlTypes.NpgsqlRange<string>),
                typeof(Range<DateTime>), Record, 0);

            var result = Assert.IsType<Range<DateTime>>(Run(expression, provider));
            Assert.Equal(2024, result.Lower!.Value.Year);
            Assert.Equal(1, result.Lower!.Value.Month);
            Assert.Equal(2, result.Lower!.Value.Day);
            Assert.Equal(2, result.Upper!.Value.Month);
        }

        [Fact]
        public void Range_ElementWithNoCoercion_ReturnsNullSoTheGeneralConversionRuns()
        {
            var expression = ProviderTypedConversions.TryBuild(typeof(NpgsqlTypes.NpgsqlRange<Uri>),
                typeof(Range<int>), Record, 0);

            Assert.Null(expression);
        }

        [Fact]
        public void Cidr_BuildsPortableCidrFromAddressAndNetmask()
        {
            var provider = new NpgsqlTypes.NpgsqlCidr { Address = IPAddress.Parse("10.1.2.0"), Netmask = 24 };

            var expression = ProviderTypedConversions.TryBuild(typeof(NpgsqlTypes.NpgsqlCidr),
                typeof(Cidr), Record, 0);

            var result = Assert.IsType<Cidr>(Run(expression, provider));
            Assert.Equal(IPAddress.Parse("10.1.2.0"), result.Network);
            Assert.Equal(24, result.PrefixLength);
        }

        [Fact]
        public void UnknownShape_ReturnsNull()
        {
            Assert.Null(ProviderTypedConversions.TryBuild(typeof(string), typeof(int), Record, 0));
        }
    }
}
