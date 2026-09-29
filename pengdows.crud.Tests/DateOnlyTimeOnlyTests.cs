using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-001: DateOnly and TimeOnly must work as plain entity properties on every database, with no
/// converter or hook in the application. Writes bind exactly as the equivalent DateTime (midnight) /
/// TimeSpan would on the same dialect, so each dialect's existing, live-verified DateTime/TimeSpan
/// handling applies unchanged. Reads accept every shape providers return for DATE/TIME columns:
/// DateTime, DateTimeOffset, TimeSpan, ISO strings (SQLite, FlatFile), and native DateOnly/TimeOnly.
/// A dialect whose driver needs something else (FlatFile's DateOnly/TimeOnly, DuckDB's TimeOnly,
/// Snowflake's DateTime) adjusts the equivalent too, so the equivalence holds on every dialect.
/// </summary>
public sealed class DateOnlyTimeOnlyTests
{
    private static readonly DateOnly SampleDate = new(2026, 9, 29);
    private static readonly TimeOnly SampleTime = new(13, 45, 30, 125);

    public static IEnumerable<object[]> AllDatabases() =>
        Enum.GetValues<SupportedDatabase>()
            .Where(db => db != SupportedDatabase.Unknown)
            .Select(db => new object[] { db });

    private static ISqlDialect CreateDialect(SupportedDatabase provider) =>
        SqlDialectFactory.CreateDialectForType(provider, new fakeDbFactory(provider),
            NullLogger<SqlDialect>.Instance);

    // What a DateOnly/TimeOnly parameter must carry: exactly what the same dialect binds for the
    // DateTime/TimeSpan equivalent.
    private static object? ExpectedDate(ISqlDialect dialect, DbType dbType) =>
        dialect.CreateDbParameter("e", dbType, SampleDate.ToDateTime(TimeOnly.MinValue)).Value;

    private static object? ExpectedTime(ISqlDialect dialect) =>
        dialect.CreateDbParameter("e", DbType.Time, SampleTime.ToTimeSpan()).Value;

    [Theory]
    [MemberData(nameof(AllDatabases))]
    public void CreateDbParameter_DateOnly_BindsLikeTheEquivalentDateTime(SupportedDatabase provider)
    {
        var dialect = CreateDialect(provider);

        foreach (var dbType in new[] { DbType.Date, DbType.DateTime, DbType.DateTime2 })
        {
            var expected = dialect.CreateDbParameter("e", dbType, SampleDate.ToDateTime(TimeOnly.MinValue));
            var actual = dialect.CreateDbParameter("a", dbType, SampleDate);

            Assert.Equal(expected.DbType, actual.DbType);
            Assert.Equal(ExpectedDate(dialect, dbType), actual.Value);
        }
    }

    [Theory]
    [MemberData(nameof(AllDatabases))]
    public void CreateDbParameter_TimeOnly_BindsLikeTheEquivalentTimeSpan(SupportedDatabase provider)
    {
        var dialect = CreateDialect(provider);

        var expected = dialect.CreateDbParameter("e", DbType.Time, SampleTime.ToTimeSpan());
        var actual = dialect.CreateDbParameter("a", DbType.Time, SampleTime);

        Assert.Equal(expected.DbType, actual.DbType);
        Assert.Equal(ExpectedTime(dialect), actual.Value);
    }

    [Theory]
    [MemberData(nameof(AllDatabases))]
    public void CreateDbParameter_NullableDateOnlyAndTimeOnly_BindNullAndValues(SupportedDatabase provider)
    {
        var dialect = CreateDialect(provider);

        Assert.Equal(DBNull.Value, dialect.CreateDbParameter("d", DbType.Date, (DateOnly?)null).Value);
        Assert.Equal(DBNull.Value, dialect.CreateDbParameter("t", DbType.Time, (TimeOnly?)null).Value);
        Assert.Equal(ExpectedDate(dialect, DbType.Date), dialect.CreateDbParameter("a", DbType.Date, (DateOnly?)SampleDate).Value);
        Assert.Equal(ExpectedTime(dialect), dialect.CreateDbParameter("a", DbType.Time, (TimeOnly?)SampleTime).Value);
    }

    [Theory]
    [MemberData(nameof(AllDatabases))]
    public void CreateDbParameter_BoxedDateOnlyAndTimeOnly_BindLikeTheirEquivalents(SupportedDatabase provider)
    {
        // The gateways read property values as object, so T is object on the CRUD path.
        var dialect = CreateDialect(provider);

        Assert.Equal(ExpectedDate(dialect, DbType.Date), dialect.CreateDbParameter("a", DbType.Date, (object)SampleDate).Value);
        Assert.Equal(ExpectedTime(dialect), dialect.CreateDbParameter("a", DbType.Time, (object)SampleTime).Value);
    }

    [Theory]
    [InlineData(DbType.Date)]
    [InlineData(DbType.DateTime)]
    [InlineData(DbType.DateTime2)]
    [InlineData(DbType.Object)]
    public void Validator_AcceptsDateOnly_ForDateTypes(DbType dbType)
    {
        DbTypeValidator.Validate(dbType, typeof(DateOnly));
        DbTypeValidator.Validate(dbType, (object)SampleDate);
    }

    [Theory]
    [InlineData(DbType.Time)]
    [InlineData(DbType.Object)]
    public void Validator_AcceptsTimeOnly_ForTimeTypes(DbType dbType)
    {
        DbTypeValidator.Validate(dbType, typeof(TimeOnly));
        DbTypeValidator.Validate(dbType, (object)SampleTime);
    }

    [Theory]
    [InlineData(DbType.Int32)]
    [InlineData(DbType.Boolean)]
    [InlineData(DbType.Binary)]
    public void Validator_RejectsDateOnlyAndTimeOnly_ForUnrelatedTypes(DbType dbType)
    {
        Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(dbType, typeof(DateOnly)));
        Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(dbType, typeof(TimeOnly)));
    }

    public static IEnumerable<object[]> DateSources() => new[]
    {
        new object[] { new DateTime(2026, 9, 29) },
        new object[] { new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc) },
        new object[] { new DateTime(2026, 9, 29, 18, 5, 0) }, // Oracle DATE carries a time part
        new object[] { new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.FromHours(-5)) },
        new object[] { "2026-09-29" },
        new object[] { "2026-09-29 00:00:00" },
        new object[] { "2026-09-29T00:00:00.0000000" },
        // An explicit offset keeps its own wall-clock date; never convert through local time
        // (found live: SQLite read DATE one day early at UTC-5). The two offsets make the result
        // independent of the machine's time zone.
        new object[] { "2026-09-29T00:00:00Z" },
        new object[] { "2026-09-29T00:00:00+14:00" },
        new object[] { "2026-09-29T23:30:00-12:00" },
        new object[] { new DateOnly(2026, 9, 29) }
    };

    public static IEnumerable<object[]> TimeSources() => new[]
    {
        new object[] { new TimeSpan(0, 13, 45, 30, 125) },
        new object[] { new DateTime(2026, 9, 29, 13, 45, 30, 125) },
        new object[] { "13:45:30.125" },
        new object[] { "13:45:30.1250000" },
        new object[] { "2026-09-29T13:45:30.125+14:00" },
        new object[] { "1970-01-01 13:45:30.125Z" },
        new object[] { new TimeOnly(13, 45, 30, 125) }
    };

    [Theory]
    [MemberData(nameof(DateSources))]
    public void Coerce_ProviderDateShapes_ToDateOnly(object source)
    {
        Assert.Equal(SampleDate, TypeCoercionHelper.Coerce(source, source.GetType(), typeof(DateOnly)));
        Assert.Equal(SampleDate, TypeCoercionHelper.Coerce(source, source.GetType(), typeof(DateOnly?)));
    }

    [Theory]
    [MemberData(nameof(TimeSources))]
    public void Coerce_ProviderTimeShapes_ToTimeOnly(object source)
    {
        Assert.Equal(SampleTime, TypeCoercionHelper.Coerce(source, source.GetType(), typeof(TimeOnly)));
        Assert.Equal(SampleTime, TypeCoercionHelper.Coerce(source, source.GetType(), typeof(TimeOnly?)));
    }

    [Fact]
    public void Coerce_UnparseableString_ToDateOnly_Throws()
    {
        Assert.ThrowsAny<Exception>(() => TypeCoercionHelper.Coerce("not a date", typeof(string), typeof(DateOnly)));
        Assert.ThrowsAny<Exception>(() => TypeCoercionHelper.Coerce("not a time", typeof(string), typeof(TimeOnly)));
    }

    [Theory]
    [MemberData(nameof(DateSources))]
    public async Task Mapper_HydratesDateOnlyFromProviderShapes(object source)
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["Value"] = source, ["Maybe"] = DBNull.Value }
        });

        var result = await DataReaderMapper.LoadAsync<DateHolder>(reader, MapperOptions.Default);

        Assert.Equal(SampleDate, Assert.Single(result).Value);
        Assert.Null(result[0].Maybe);
    }

    [Theory]
    [MemberData(nameof(TimeSources))]
    public async Task Mapper_HydratesTimeOnlyFromProviderShapes(object source)
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["Value"] = source, ["Maybe"] = source }
        });

        var result = await DataReaderMapper.LoadAsync<TimeHolder>(reader, MapperOptions.Default);

        Assert.Equal(SampleTime, Assert.Single(result).Value);
        Assert.Equal(SampleTime, result[0].Maybe);
    }

    [Theory]
    [MemberData(nameof(AllDatabases))]
    public void BuildCreate_DateOnlyAndTimeOnlyProperties_BindOnEveryDatabase(SupportedDatabase provider)
    {
        var factory = new fakeDbFactory(provider);
        using var context = new DatabaseContext($"Data Source=test;EmulatedProduct={provider}", factory);
        var gateway = new TableGateway<CalendarEntity, int>(context);
        var dialect = context.Dialect;

        using var container = gateway.BuildCreate(new CalendarEntity
        {
            Id = 1,
            Day = SampleDate,
            At = SampleTime,
            MaybeDay = null,
            MaybeAt = SampleTime
        });

        var values = Enumerable.Range(0, container.ParameterCount)
            .Select(i => container.GetParameterValue("i" + i))
            .ToList();
        Assert.Contains(ExpectedDate(dialect, DbType.Date), values);
        Assert.Contains(ExpectedTime(dialect), values);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite, "2026-09-29", "13:45:30.125")]
    [InlineData(SupportedDatabase.PostgreSql, null, null)]
    [InlineData(SupportedDatabase.Oracle, null, null)]
    public async Task RetrieveOne_DateOnlyAndTimeOnlyProperties_Hydrate(SupportedDatabase provider,
        string? dateText, string? timeText)
    {
        object day = dateText ?? (object)SampleDate.ToDateTime(TimeOnly.MinValue);
        object at = timeText ?? (object)SampleTime.ToTimeSpan();
        var factory = new fakeDbFactory(provider);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = provider });
        var execConn = new fakeDbConnection { EmulatedProduct = provider };
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = 1,
                ["day"] = day,
                ["at"] = at,
                ["maybe_day"] = null,
                ["maybe_at"] = at
            }
        });
        factory.Connections.Add(execConn);
        using var context = new DatabaseContext($"Data Source=test;EmulatedProduct={provider}", factory);
        var gateway = new TableGateway<CalendarEntity, int>(context);

        var actual = await gateway.RetrieveOneAsync(1);

        Assert.NotNull(actual);
        Assert.Equal(SampleDate, actual!.Day);
        Assert.Equal(SampleTime, actual.At);
        Assert.Null(actual.MaybeDay);
        Assert.Equal(SampleTime, actual.MaybeAt);
    }

    private sealed class DateHolder
    {
        public DateOnly Value { get; set; }
        public DateOnly? Maybe { get; set; }
    }

    private sealed class TimeHolder
    {
        public TimeOnly Value { get; set; }
        public TimeOnly? Maybe { get; set; }
    }

    [Table("calendar")]
    private sealed class CalendarEntity
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("day", DbType.Date)] public DateOnly Day { get; set; }
        [Column("at", DbType.Time)] public TimeOnly At { get; set; }
        [Column("maybe_day", DbType.Date)] public DateOnly? MaybeDay { get; set; }
        [Column("maybe_at", DbType.Time)] public TimeOnly? MaybeAt { get; set; }
    }
}
