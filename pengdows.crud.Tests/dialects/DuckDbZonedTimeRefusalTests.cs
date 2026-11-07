using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// DuckDB has no time-with-zone column that can hold a DateTimeOffset's offset, so a DateTimeOffset declared as a time is refused
/// at the parameter, by name, rather than bound as a value that loses the offset.
/// </summary>
public sealed class DuckDbZonedTimeRefusalTests
{
    [Fact]
    public void ADateTimeOffsetDeclaredAsATimeIsRefusedOnDuckDb()
    {
        var dialect = new DuckDbDialect(new fakeDbFactory(SupportedDatabase.DuckDB), NullLogger.Instance);
        var value = new DateTimeOffset(2026, 10, 5, 13, 45, 30, TimeSpan.FromHours(2));

        var ex = Assert.Throws<ArgumentException>(() => dialect.CreateDbParameter("t", DbType.Time, value));

        Assert.Contains("DbType.Time", ex.Message);
    }
}
