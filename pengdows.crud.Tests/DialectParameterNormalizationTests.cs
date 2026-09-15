using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DialectParameterNormalizationTests
{
    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    public void MySqlFamily_NormalizesBooleanAndDateTimeOffset(SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var dialect = database == SupportedDatabase.MariaDb
            ? new MariaDbDialect(factory, NullLogger.Instance)
            : new MySqlDialect(factory, NullLogger.Instance);
        var timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5));

        var boolean = dialect.CreateDbParameter("flag", DbType.Boolean, true);
        var temporal = dialect.CreateDbParameter("created", DbType.DateTimeOffset, timestamp);

        Assert.Equal(DbType.Byte, boolean.DbType);
        Assert.Equal((byte)1, boolean.Value);
        Assert.Equal(DbType.DateTime, temporal.DbType);
        Assert.Equal(timestamp.UtcDateTime, temporal.Value);
    }

    [Fact]
    public void PostgreSql_NormalizesDateTimeOffsetToUtc()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var dialect = new PostgreSqlDialect(factory, NullLogger.Instance);
        var timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5));

        var parameter = dialect.CreateDbParameter("created", DbType.DateTimeOffset, timestamp);

        Assert.Equal(DbType.DateTime, parameter.DbType);
        Assert.Equal(timestamp.UtcDateTime, parameter.Value);
    }
}
