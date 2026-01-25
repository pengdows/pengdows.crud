using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests
{
    public class PostgreSqlRangeConverterBranchTests
    {
        [Fact]
        public void ConvertToProvider_ReturnsValueOutsidePostgres()
        {
            var converter = new PostgreSqlRangeConverter<int>();
            var range = new Range<int>(1, 10);

            var result = converter.ToProviderValue(range, SupportedDatabase.SqlServer);

            Assert.Equal(range, result);
        }

        [Fact]
        public void ConvertToProvider_FormatsForPostgres()
        {
            var converter = new PostgreSqlRangeConverter<int>();
            var range = new Range<int>(1, 10, true, false);

            var result = converter.ToProviderValue(range, SupportedDatabase.PostgreSql);

            Assert.Equal("[1,10)", result);
        }

        [Fact]
        public void TryConvertFromProvider_ParsesStringAndTuple()
        {
            var converter = new PostgreSqlRangeConverter<int>();

            Assert.True(converter.TryConvertFromProvider("[1,10)", SupportedDatabase.PostgreSql, out var parsed));
            Assert.Equal(new Range<int>(1, 10, true, false), parsed);

            var tuple = Tuple.Create<int?, int?>(2, 5);
            Assert.True(converter.TryConvertFromProvider(tuple, SupportedDatabase.PostgreSql, out var tupleRange));
            Assert.Equal(new Range<int>(2, 5), tupleRange);
        }

        [Fact]
        public void TryConvertFromProvider_HandlesNpgsqlRange()
        {
            var converter = new PostgreSqlRangeConverter<int>();
            var npgsqlRange = new NpgsqlTypes.NpgsqlRange<int>
            {
                LowerBound = 1,
                UpperBound = 10,
                LowerBoundIsInclusive = true,
                UpperBoundIsInclusive = false,
                LowerBoundInfinite = false,
                UpperBoundInfinite = false
            };

            Assert.True(converter.TryConvertFromProvider(npgsqlRange, SupportedDatabase.PostgreSql, out var result));
            Assert.Equal(new Range<int>(1, 10, true, false), result);
        }

        [Fact]
        public void TryConvertFromProvider_Invalid_ReturnsFalse()
        {
            var converter = new PostgreSqlRangeConverter<int>();
            Assert.False(converter.TryConvertFromProvider("not-a-range", SupportedDatabase.PostgreSql, out _));
        }
    }
}

namespace NpgsqlTypes
{
    public sealed class NpgsqlRange<T>
    {
        public T? LowerBound { get; set; }
        public T? UpperBound { get; set; }
        public bool LowerBoundIsInclusive { get; set; }
        public bool UpperBoundIsInclusive { get; set; }
        public bool LowerBoundInfinite { get; set; }
        public bool UpperBoundInfinite { get; set; }
    }
}