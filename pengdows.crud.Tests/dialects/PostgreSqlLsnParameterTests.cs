using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// TYPE-002, found live on PostgreSQL (Npgsql 9): Npgsql can't infer pg_lsn from an
/// NpgsqlLogSequenceNumber ("Writing values of 'NpgsqlTypes.NpgsqlLogSequenceNumber' is not
/// supported for parameters having no NpgsqlDbType or DataTypeName"), so the dialect names it.
/// </summary>
public class PostgreSqlLsnParameterTests
{
    [Fact]
    public void CreateDbParameter_LogSequenceNumber_NamesThePgLsnType()
    {
        var dialect = new PostgreSqlDialect(new NamedTypeFactory(), NullLogger<PostgreSqlDialect>.Instance);

        var parameter = Assert.IsType<NamedTypeParameter>(
            dialect.CreateDbParameter("p", DbType.Object, new NpgsqlTypes.NpgsqlLogSequenceNumber(42)));

        Assert.Equal("pg_lsn", parameter.DataTypeName);
    }

    // TYPE-002, found live: a DateTimeOffset declared DbType.DateTimeOffset reached a timetz column as
    // timestamptz, which "WHERE v = @p" can't compare ("operator does not exist: time with time
    // zone = timestamp with time zone"). Declared DbType.Time it is a time with an offset: timetz,
    // offset kept (not converted to a UTC DateTime as for timestamptz).
    [Fact]
    public void CreateDbParameter_DateTimeOffsetDeclaredTime_IsTimeTzWithItsOffset()
    {
        var dialect = new PostgreSqlDialect(new NamedTypeFactory(), NullLogger<PostgreSqlDialect>.Instance);
        var value = new System.DateTimeOffset(1, 1, 2, 13, 45, 30, System.TimeSpan.FromHours(-5));

        var parameter = Assert.IsType<NamedTypeParameter>(dialect.CreateDbParameter("p", DbType.Time, value));

        Assert.Equal("time with time zone", parameter.DataTypeName);
        Assert.Equal(value, parameter.Value);
        Assert.Equal(System.TimeSpan.FromHours(-5), ((System.DateTimeOffset)parameter.Value!).Offset);
    }

    private sealed class NamedTypeFactory : DbProviderFactory
    {
        public override DbParameter CreateParameter() => new NamedTypeParameter();
        public override DbConnection CreateConnection() => new fakeDbConnection();
        public override DbCommand CreateCommand() => new fakeDbCommand();
    }

    private sealed class NamedTypeParameter : fakeDbParameter
    {
        public string? DataTypeName { get; set; }
    }
}
