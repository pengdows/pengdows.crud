using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Npgsql maps DbType.DateTime2 to timestamp without time zone and refuses a DateTime with
/// Kind=Utc for it ("Cannot write DateTime with Kind=UTC to PostgreSQL type 'timestamp without
/// time zone'"). pengdows stores UTC wall time there, so the UTC instant is sent unchanged with
/// Kind=Unspecified. DbType.DateTime (timestamptz in Npgsql) keeps Kind=Utc.
/// </summary>
public class PostgreSqlDateTime2KindTests
{
    private static readonly DateTime Utc = new(2026, 10, 1, 13, 45, 30, 123, DateTimeKind.Utc);

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void CreateDbParameter_DateTime2WithUtcKind_SendsTheSameInstantUnspecified(SupportedDatabase product)
    {
        var dialect = Dialect(product);

        var parameter = dialect.CreateDbParameter("p", DbType.DateTime2, Utc);

        var value = Assert.IsType<DateTime>(parameter.Value);
        Assert.Equal(Utc.Ticks, value.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, value.Kind);
    }

    [Fact]
    public void CreateDbParameter_DateTimeWithUtcKind_KeepsUtc()
    {
        var parameter = Dialect(SupportedDatabase.PostgreSql).CreateDbParameter("p", DbType.DateTime, Utc);

        Assert.Equal(DateTimeKind.Utc, Assert.IsType<DateTime>(parameter.Value).Kind);
    }

    // Found by the live type matrix 2026-10-06: an Unspecified DateTime (UTC, docs/utc-and-time.md)
    // declared DbType.DateTime was refused by Npgsql ("Cannot write DateTime with Kind=Unspecified to
    // PostgreSQL type 'timestamp with time zone'") on PostgreSQL, CockroachDB, YugabyteDB and Spanner.
    // It goes as Kind=Utc; a Local one as its UTC instant, on both DbTypes.
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    [InlineData(SupportedDatabase.Spanner)]
    public void CreateDbParameter_DateTimeUnspecifiedOrLocal_SendsTheUtcInstant(SupportedDatabase product)
    {
        var dialect = Dialect(product);
        var unspecified = DateTime.SpecifyKind(Utc, DateTimeKind.Unspecified);
        var local = Utc.ToLocalTime();

        foreach (var value in new[] { unspecified, local })
        {
            var timestamptz = Assert.IsType<DateTime>(dialect.CreateDbParameter("p", DbType.DateTime, value).Value);
            Assert.Equal(DateTimeKind.Utc, timestamptz.Kind);
            Assert.Equal(Utc.Ticks, timestamptz.Ticks);

            var timestamp = Assert.IsType<DateTime>(dialect.CreateDbParameter("p", DbType.DateTime2, value).Value);
            Assert.Equal(DateTimeKind.Unspecified, timestamp.Kind);
            Assert.Equal(Utc.Ticks, timestamp.Ticks);
        }
    }

    private static ISqlDialect Dialect(SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product));
        return context.GetDialect();
    }
}
