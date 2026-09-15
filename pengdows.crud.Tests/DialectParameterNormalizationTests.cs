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

        // DbType is switched to Byte (TINYINT(1) compatibility) but parameter.Value itself is
        // NOT converted away from the raw C# bool through this call path (SqlDialect.CreateDbParameter<T>)
        // — CoercionRegistry's BooleanCoercion always succeeds first in
        // AdvancedTypeRegistry.TryConfigureParameterForDialect's `||` chain, so
        // ParameterBindingRules.ApplyBooleanNormalization's (byte)1/(byte)0 conversion never
        // runs. See ProviderParameterFactoryTests.TryConfigureParameter_ConfiguresMySqlBoolean,
        // which already locks in this exact DbType-changes-but-Value-stays-bool behavior as
        // expected.
        Assert.Equal(DbType.Byte, boolean.DbType);
        Assert.Equal(true, boolean.Value);
        // Unlike PostgreSql (whose Npgsql driver rejects non-UTC DateTimeOffset for
        // timestamptz — see EnsureDateTimeParameterization / PostgreSql_NormalizesDateTimeOffsetToUtc
        // below), this branch's parameter-binding pipeline does not special-case MySql/MariaDb for
        // DateTimeOffset: it passes the value through unconverted rather than forcing UTC DateTime.
        Assert.Equal(DbType.DateTimeOffset, temporal.DbType);
        Assert.Equal(timestamp, temporal.Value);
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
