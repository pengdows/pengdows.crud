using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// DuckDB has a TIME WITH TIME ZONE column (TIMETZ): the server accepts it and reads it back as a DateTimeOffset with its offset
/// (results/DuckDB.jsonl). A DateTimeOffset declared as a time is bound as its time and offset text, which TIMETZ casts to.
/// </summary>
public sealed class DuckDbZonedTimeBindingTests
{
    private static DuckDbDialect Dialect() => new(new fakeDbFactory(SupportedDatabase.DuckDB), NullLogger.Instance);

    [Fact]
    public void ADateTimeOffsetDeclaredAsATimeBindsItsTimeAndOffsetAsText()
    {
        var value = new DateTimeOffset(2026, 10, 5, 13, 45, 30, TimeSpan.FromHours(2));

        var parameter = Dialect().CreateDbParameter("t", DbType.Time, value);

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("13:45:30.000000+02:00", parameter.Value);
    }

    [Fact]
    public void TheOffsetSurvivesANegativeHalfHourZone()
    {
        var value = new DateTimeOffset(2026, 1, 1, 8, 0, 0, 250, TimeSpan.FromMinutes(-330));

        var parameter = Dialect().CreateDbParameter("t", DbType.Time, value);

        Assert.Equal("08:00:00.250000-05:30", parameter.Value);
    }
}
