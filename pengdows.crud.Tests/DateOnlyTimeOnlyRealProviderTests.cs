using System;
using System.Data;
using System.Threading.Tasks;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-001 against the real in-process providers (Microsoft.Data.Sqlite, DuckDB.NET): DateOnly and
/// TimeOnly properties, including NULLs, round-trip through the gateway and filter in a WHERE. Found
/// live: SQLite read DATE back one day early in a negative-offset time zone, and a NULL TIME column
/// threw NullReferenceException in the compiled mapper.
/// </summary>
[Collection("SqliteSerial")]
public sealed class DateOnlyTimeOnlyRealProviderTests
{
    public static TheoryData<string> Providers() => new() { "Sqlite", "DuckDB" };

    private static DatabaseContext CreateContext(string provider) => provider switch
    {
        "Sqlite" => new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance),
        _ => new DatabaseContext("Data Source=:memory:", DuckDBClientFactory.Instance)
    };

    private static string DateType(string provider) => provider == "Sqlite" ? "TEXT" : "DATE";
    private static string TimeType(string provider) => provider == "Sqlite" ? "TEXT" : "TIME";

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task DateOnlyAndTimeOnly_RoundTripWithNullsAndFilter(string provider)
    {
        await using var context = CreateContext(provider);
        Assert.Equal(provider, context.Product.ToString());
        Assert.Equal(pengdows.crud.enums.DbMode.SingleConnection, context.ConnectionMode);
        await using (var ddl = context.CreateSqlContainer(
                         $"CREATE TABLE calendar (id INTEGER PRIMARY KEY, event_day {DateType(provider)} NOT NULL, " +
                         $"maybe_day {DateType(provider)}, event_time {TimeType(provider)} NOT NULL, " +
                         $"maybe_time {TimeType(provider)})"))
        {
            await ddl.ExecuteNonQueryAsync();
        }

        var gateway = new TableGateway<Calendar, int>(context);
        var rows = new[]
        {
            new Calendar { Id = 1, Day = new DateOnly(2026, 9, 29), MaybeDay = new DateOnly(2000, 2, 29),
                At = new TimeOnly(13, 45, 30), MaybeAt = new TimeOnly(6, 0, 1) },
            new Calendar { Id = 2, Day = new DateOnly(1970, 1, 1), MaybeDay = null,
                At = new TimeOnly(0, 0, 0), MaybeAt = null },
            new Calendar { Id = 3, Day = new DateOnly(9999, 12, 31), MaybeDay = new DateOnly(1999, 12, 31),
                At = new TimeOnly(23, 59, 59), MaybeAt = new TimeOnly(12, 0, 0) }
        };

        foreach (var row in rows)
        {
            Assert.True(await gateway.CreateAsync(row));
        }

        foreach (var row in rows)
        {
            var actual = await gateway.RetrieveOneAsync(row.Id);
            Assert.NotNull(actual);
            Assert.Equal(row.Day, actual!.Day);
            Assert.Equal(row.MaybeDay, actual.MaybeDay);
            Assert.Equal(row.At, actual.At);
            Assert.Equal(row.MaybeAt, actual.MaybeAt);
        }

        await using var sc = gateway.BuildBaseRetrieve("c");
        sc.Query.Append(" WHERE ").Append(sc.WrapObjectName("c.event_day")).Append(" = ");
        var day = sc.AddParameterWithValue("day", DbType.Date, new DateOnly(1970, 1, 1));
        sc.Query.Append(sc.MakeParameterName(day));
        sc.Query.Append(" AND ").Append(sc.WrapObjectName("c.event_time")).Append(" = ");
        var at = sc.AddParameterWithValue("at", DbType.Time, new TimeOnly(0, 0, 0));
        sc.Query.Append(sc.MakeParameterName(at));
        var matched = await gateway.LoadListAsync(sc);
        Assert.Equal(2, Assert.Single(matched).Id);
    }

    [Table("calendar")]
    private sealed class Calendar
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("event_day", DbType.Date)] public DateOnly Day { get; set; }
        [Column("maybe_day", DbType.Date)] public DateOnly? MaybeDay { get; set; }
        [Column("event_time", DbType.Time)] public TimeOnly At { get; set; }
        [Column("maybe_time", DbType.Time)] public TimeOnly? MaybeAt { get; set; }
    }
}
