using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// TYPE-001: DateOnly and TimeOnly entity properties round-trip through TableGateway on every
/// database — create, retrieve, and a WHERE predicate bound from a DateOnly/TimeOnly value — with no
/// converter or hook in the application. Each column uses the database's own date / time-of-day
/// type. Values are whole seconds: Db2's TIME has no fractional part and Sybase ASE's TIME ticks
/// are 1/300 s, so fractional precision is a separate per-type concern.
/// </summary>
[Collection("IntegrationTests")]
public class DateOnlyTimeOnlyRoundTripTests : DatabaseTestBase
{
    private const string DaysTable = "calendar_days";
    private const string TimesTable = "calendar_times";

    // Spanner (PostgreSQL interface) has no time-of-day column type at all: no TIME, and INTERVAL
    // is query-only. Every other database has one.
    private static readonly SupportedDatabase[] ProvidersWithTimeOfDayType =
        Enum.GetValues<SupportedDatabase>()
            .Where(p => p is not SupportedDatabase.Unknown and not SupportedDatabase.Spanner)
            .ToArray();

    public DateOnlyTimeOnlyRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await ExecuteAsync(context, CreateDaysSql(provider, context));
        if (ProvidersWithTimeOfDayType.Contains(provider))
        {
            await ExecuteAsync(context, CreateTimesSql(provider, context));
        }
    }

    [SkippableFact]
    public async Task DateOnly_RoundTripsAndFiltersOnEveryDatabase()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<CalendarDay, int>(context);
            var rows = new[]
            {
                new CalendarDay { Id = 1, Day = new DateOnly(2026, 9, 29), MaybeDay = new DateOnly(2000, 2, 29) },
                new CalendarDay { Id = 2, Day = new DateOnly(1970, 1, 1), MaybeDay = null },
                new CalendarDay { Id = 3, Day = new DateOnly(9999, 12, 31), MaybeDay = new DateOnly(1999, 12, 31) }
            };

            foreach (var row in rows)
            {
                Assert.True(await gateway.CreateAsync(row, context));
            }

            foreach (var row in rows)
            {
                var actual = await gateway.RetrieveOneAsync(row.Id, context);
                Assert.NotNull(actual);
                Assert.Equal(row.Day, actual!.Day);
                Assert.Equal(row.MaybeDay, actual.MaybeDay);
            }

            await using var sc = gateway.BuildBaseRetrieve("d", context);
            sc.Query.Append(" WHERE ").Append(sc.WrapObjectName("d.event_day")).Append(" = ");
            var p = sc.AddParameterWithValue("day", DbType.Date, new DateOnly(1970, 1, 1));
            sc.Query.Append(sc.MakeParameterName(p));
            var matched = await gateway.LoadListAsync(sc);
            Assert.Equal(2, Assert.Single(matched).Id);
        });
    }

    [SkippableFact]
    public async Task TimeOnly_RoundTripsAndFiltersOnEveryDatabaseWithATimeOfDayType()
    {
        await RunTestAgainstProvidersAsync(ProvidersWithTimeOfDayType, async (provider, context) =>
        {
            var gateway = new TableGateway<CalendarTime, int>(context);
            var rows = new[]
            {
                new CalendarTime { Id = 1, At = new TimeOnly(13, 45, 30), MaybeAt = new TimeOnly(6, 0, 1) },
                new CalendarTime { Id = 2, At = new TimeOnly(0, 0, 0), MaybeAt = null },
                new CalendarTime { Id = 3, At = new TimeOnly(23, 59, 59), MaybeAt = new TimeOnly(12, 0, 0) }
            };

            foreach (var row in rows)
            {
                Assert.True(await gateway.CreateAsync(row, context));
            }

            foreach (var row in rows)
            {
                var actual = await gateway.RetrieveOneAsync(row.Id, context);
                Assert.NotNull(actual);
                Assert.Equal(row.At, actual!.At);
                Assert.Equal(row.MaybeAt, actual.MaybeAt);
            }

            await using var sc = gateway.BuildBaseRetrieve("t", context);
            sc.Query.Append(" WHERE ").Append(sc.WrapObjectName("t.event_time")).Append(" = ");
            var p = sc.AddParameterWithValue("at", DbType.Time, new TimeOnly(23, 59, 59));
            sc.Query.Append(sc.MakeParameterName(p));
            var matched = await gateway.LoadListAsync(sc);
            Assert.Equal(3, Assert.Single(matched).Id);
        });
    }

    private static async Task ExecuteAsync(IDatabaseContext context, string sql)
    {
        await using var sc = context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }

    private static string IdType(SupportedDatabase provider) => provider switch
    {
        SupportedDatabase.Spanner => "BIGINT",
        _ => "INTEGER"
    };

    private static string DateType(SupportedDatabase provider) => provider switch
    {
        SupportedDatabase.Sqlite => "TEXT",
        _ => "DATE"
    };

    private static string TimeType(SupportedDatabase provider) => provider switch
    {
        SupportedDatabase.Sqlite => "TEXT",
        SupportedDatabase.Oracle => "INTERVAL DAY(0) TO SECOND(0)",
        SupportedDatabase.Informix => "DATETIME HOUR TO SECOND",
        _ => "TIME"
    };

    // Sybase ASE columns are NOT NULL unless declared NULL; several other engines reject an
    // explicit NULL constraint, so it is only written where it is required.
    private static string Nullable(SupportedDatabase provider) =>
        provider == SupportedDatabase.SybaseASE ? " NULL" : string.Empty;

    private static string Suffix(SupportedDatabase provider) =>
        provider == SupportedDatabase.FlatFile ? " WITH (NULLTOKEN = '<<NULL>>')" : string.Empty;

    private static string CreateDaysSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var w = (string name) => context.WrapObjectName(name);
        return $"CREATE TABLE {IntegrationObjectNameHelper.Table(context, DaysTable)} (" +
               $"{w("id")} {IdType(provider)} NOT NULL PRIMARY KEY, " +
               $"{w("event_day")} {DateType(provider)} NOT NULL, " +
               $"{w("maybe_day")} {DateType(provider)}{Nullable(provider)})" + Suffix(provider);
    }

    private static string CreateTimesSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var w = (string name) => context.WrapObjectName(name);
        return $"CREATE TABLE {IntegrationObjectNameHelper.Table(context, TimesTable)} (" +
               $"{w("id")} {IdType(provider)} NOT NULL PRIMARY KEY, " +
               $"{w("event_time")} {TimeType(provider)} NOT NULL, " +
               $"{w("maybe_time")} {TimeType(provider)}{Nullable(provider)})" + Suffix(provider);
    }

    [Table(DaysTable)]
    public sealed class CalendarDay
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("event_day", DbType.Date)] public DateOnly Day { get; set; }
        [Column("maybe_day", DbType.Date)] public DateOnly? MaybeDay { get; set; }
    }

    [Table(TimesTable)]
    public sealed class CalendarTime
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("event_time", DbType.Time)] public TimeOnly At { get; set; }
        [Column("maybe_time", DbType.Time)] public TimeOnly? MaybeAt { get; set; }
    }
}
