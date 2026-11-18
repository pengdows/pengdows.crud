using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.RangeConverterBranches
{
    public class PostgreSqlRangeConverterNpgsqlBranchTests
    {
        [Fact]
        public void TryConvertFromProvider_EmptyNpgsqlRange_IsTheEmptyRange()
        {
            var provider = new NpgsqlTypes.NpgsqlRange<int> { IsEmpty = true };

            Assert.True(new PostgreSqlRangeConverter<int>().TryConvertFromProvider(provider, SupportedDatabase.PostgreSql,
                out var result));
            Assert.True(result.IsEmpty);
        }

        [Fact]
        public void TryConvertFromProvider_BoundOfAnotherNumericType_IsCoercedToTheTarget()
        {
            // long has no typed coercion to int, so each bound is read boxed and coerced.
            var provider = new NpgsqlTypes.NpgsqlRange<long>
            {
                LowerBound = 5L,
                UpperBound = 9L,
                LowerBoundIsInclusive = true,
                UpperBoundIsInclusive = false,
            };

            Assert.True(new PostgreSqlRangeConverter<int>().TryConvertFromProvider(provider, SupportedDatabase.PostgreSql,
                out var result));
            Assert.Equal(5, result.Lower);
            Assert.Equal(9, result.Upper);
        }

        [Fact]
        public void TryConvertFromProvider_StringBoundTypedToDateTime_IsConvertedByTheTypedCoercion()
        {
            var provider = new NpgsqlTypes.NpgsqlRange<string>
            {
                LowerBound = "2024-01-02T00:00:00Z",
                UpperBound = "2024-02-01T00:00:00Z",
                LowerBoundIsInclusive = true,
                UpperBoundIsInclusive = false,
            };

            Assert.True(new PostgreSqlRangeConverter<System.DateTime>().TryConvertFromProvider(provider,
                SupportedDatabase.PostgreSql, out var result));
            Assert.Equal(new System.DateTime(2024, 1, 2, 0, 0, 0, System.DateTimeKind.Utc), result.Lower!.Value.ToUniversalTime());
        }
    

        [Fact]
        public void TryConvertFromProvider_NullBoundNotFlaggedInfinite_ReadsAsAnAbsentBound()
        {
            var provider = new NpgsqlTypes.NpgsqlRange<string>
            {
                LowerBound = null,
                LowerBoundInfinite = false,
                UpperBound = "9",
                LowerBoundIsInclusive = true,
                UpperBoundIsInclusive = false,
            };

            Assert.True(new PostgreSqlRangeConverter<int>().TryConvertFromProvider(provider, SupportedDatabase.PostgreSql,
                out var result));
            Assert.Null(result.Lower);
            Assert.Equal(9, result.Upper);
        }
    }
}
