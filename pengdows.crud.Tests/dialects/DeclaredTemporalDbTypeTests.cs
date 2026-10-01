using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Found forward-porting 2.0.6's live DateOnlyTimeOnlyRoundTripTests to 3.0: a DateTime (or a
/// DateOnly, which binds as its midnight DateTime) declared <see cref="DbType.Date"/> went through the
/// advanced binding pipeline, which types a DateTime by its CLR type, so it reached the provider as
/// DbType.DateTime: Npgsql then sends timestamptz and rejects an Unspecified DateTime, and SqlClient
/// sends datetime and rejects DateOnly.MinValue ("SqlDateTime overflow"). The declared column type
/// wins for a date or time of day, as on 2.0.6.
/// </summary>
public sealed class DeclaredTemporalDbTypeTests
{
    private static SqlDialect Dialect(SupportedDatabase database) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database),
            NullLogger<SqlDialect>.Instance);

    public static TheoryData<SupportedDatabase> ClientServerDatabases => new()
    {
        SupportedDatabase.PostgreSql, SupportedDatabase.CockroachDb, SupportedDatabase.YugabyteDb,
        SupportedDatabase.Spanner, SupportedDatabase.SqlServer, SupportedDatabase.MySql, SupportedDatabase.MariaDb
    };

    [Theory]
    [MemberData(nameof(ClientServerDatabases))]
    public void DateOnlyDeclaredAsDate_BindsAsDate(SupportedDatabase database)
    {
        var parameter = Dialect(database).CreateDbParameter<object>("d", DbType.Date, new DateOnly(1970, 1, 1));

        Assert.Equal(DbType.Date, parameter.DbType);
        Assert.Equal(new DateTime(1970, 1, 1), parameter.Value);
    }

    [Theory]
    [MemberData(nameof(ClientServerDatabases))]
    public void DateTimeDeclaredAsDate_BindsAsDate(SupportedDatabase database)
    {
        var parameter = Dialect(database).CreateDbParameter<object>("d", DbType.Date, new DateTime(1970, 1, 1));

        Assert.Equal(DbType.Date, parameter.DbType);
    }

    [Fact]
    public void SqlClient_DateOnlyMinValue_BindsAsSqlDate()
    {
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.SqlServer,
            SqlClientFactory.Instance, NullLogger<SqlDialect>.Instance);

        var parameter = (SqlParameter)dialect.CreateDbParameter<object>("d", DbType.Date, DateOnly.MinValue);

        Assert.Equal(SqlDbType.Date, parameter.SqlDbType);
    }

    [Fact]
    public void DateTimeDeclaredAsDateTime_KeepsTheProviderSpecificBinding()
    {
        // Spanner has no plain timestamp: a DateTime declared DbType.DateTime must still go through
        // the pipeline that marks it timestamptz, not be rebound by its declared type.
        var parameter = Dialect(SupportedDatabase.Spanner)
            .CreateDbParameter<object>("t", DbType.DateTime, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        Assert.Equal(DbType.DateTime, parameter.DbType);
    }
}
