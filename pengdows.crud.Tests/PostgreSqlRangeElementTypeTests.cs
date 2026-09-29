using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-009: every built-in PostgreSQL range type maps to a <see cref="Range{T}"/> entity property
/// with no application code — daterange (<c>Range&lt;DateOnly&gt;</c>), numrange
/// (<c>Range&lt;decimal&gt;</c>) and tstzrange (<c>Range&lt;DateTimeOffset&gt;</c>) alongside the
/// int4range/int8range/tsrange already supported. Npgsql may return a range whose element type
/// differs from the property's (daterange as <c>NpgsqlRange&lt;DateTime&gt;</c>, tstzrange as
/// <c>NpgsqlRange&lt;DateTime&gt;</c> in UTC), so each bound is coerced to the property's type.
/// </summary>
public sealed class PostgreSqlRangeElementTypeTests
{
    [Theory]
    [InlineData(typeof(Range<DateOnly>))]
    [InlineData(typeof(Range<decimal>))]
    [InlineData(typeof(Range<DateTimeOffset>))]
    public void NewRangeTypes_AreMappedForThePostgreSqlFamily(Type rangeType)
    {
        Assert.True(AdvancedTypeRegistry.Shared.IsMappedType(rangeType));
        foreach (var provider in new[] { SupportedDatabase.PostgreSql, SupportedDatabase.CockroachDb, SupportedDatabase.YugabyteDb })
        {
            Assert.NotNull(AdvancedTypeRegistry.Shared.GetMapping(rangeType, provider));
        }
    }

    [Fact]
    public void CreateDbParameter_NewRangeTypes_BindOnPostgreSql()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.PostgreSql,
            new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<SqlDialect>.Instance);

        var values = new object[]
        {
            new Range<DateOnly>(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), true, false),
            new Range<decimal>(1.5m, 9.25m, true, true),
            new Range<DateTimeOffset>(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), true, false)
        };

        foreach (var value in values)
        {
            var parameter = dialect.CreateDbParameter("r", DbType.Object, value);
            Assert.NotNull(parameter.Value);
            Assert.NotEqual(DBNull.Value, parameter.Value);
            // Converted for the provider (NpgsqlRange<T>, or range text without Npgsql), never the
            // raw value object, which no provider can bind.
            Assert.False(parameter.Value!.GetType().IsGenericType &&
                         parameter.Value.GetType().GetGenericTypeDefinition() == typeof(Range<>));
        }
    }

    [Fact]
    public void DateRange_WithoutNpgsql_FormatsIsoDates()
    {
        // Without Npgsql loaded the converter falls back to range text, which PostgreSQL parses by
        // DateStyle unless it is ISO; ISO dates are unambiguous under every DateStyle.
        var converter = new PostgreSqlRangeConverter<DateOnly>();
        var range = new Range<DateOnly>(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), true, false);

        Assert.Equal("[2026-09-01,2026-10-01)", converter.ToProviderValue(range, SupportedDatabase.PostgreSql));
    }

    [Fact]
    public void DateRange_ReadsFromNpgsqlRangeOfDateTime()
    {
        var converter = new PostgreSqlRangeConverter<DateOnly>();
        var provider = new NpgsqlTypes.NpgsqlRange<DateTime>
        {
            LowerBound = new DateTime(2026, 9, 1),
            UpperBound = new DateTime(2026, 10, 1),
            LowerBoundIsInclusive = true
        };

        Assert.True(converter.TryConvertFromProvider(provider, SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(new Range<DateOnly>(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), true, false), result);
    }

    [Fact]
    public void TstzRange_ReadsFromNpgsqlRangeOfUtcDateTime()
    {
        var converter = new PostgreSqlRangeConverter<DateTimeOffset>();
        var provider = new NpgsqlTypes.NpgsqlRange<DateTime>
        {
            LowerBound = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            UpperBoundInfinite = true,
            LowerBoundIsInclusive = true
        };

        Assert.True(converter.TryConvertFromProvider(provider, SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), result.Lower);
        Assert.False(result.HasUpperBound);
    }

    [Fact]
    public void NumRange_ReadsFromNpgsqlRangeOfDecimal()
    {
        var converter = new PostgreSqlRangeConverter<decimal>();
        var provider = new NpgsqlTypes.NpgsqlRange<decimal>
        {
            LowerBound = 1.5m,
            UpperBound = 9.25m,
            LowerBoundIsInclusive = true,
            UpperBoundIsInclusive = true
        };

        Assert.True(converter.TryConvertFromProvider(provider, SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(new Range<decimal>(1.5m, 9.25m, true, true), result);
    }

    [Fact]
    public void RangeText_ParsesDateOnlyBounds()
    {
        Assert.Equal(new Range<DateOnly>(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), true, false),
            Range<DateOnly>.Parse("[2026-09-01,2026-10-01)"));
    }

    [Theory]
    [InlineData("[2026-09-01,2026-10-01)")]
    public void Coerce_RangeTextToDateOnlyRange(string text)
    {
        var result = TypeCoercionHelper.Coerce(text, typeof(string), typeof(Range<DateOnly>));

        Assert.Equal(new Range<DateOnly>(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), true, false), result);
    }
}
