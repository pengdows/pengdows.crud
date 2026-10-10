using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A dialect that can probe a server's connection limit also knows the port that server listens on when a
/// connection string names none, so "Host=db1" and "Host=db1;Port=5432" are recognised as one server.
/// Engines with no fixed port, or no probe, say null rather than guess.
/// </summary>
public sealed class DialectDefaultServerPortTests
{
    public static TheoryData<string, int?> Ports => new()
    {
        { nameof(PostgreSqlDialect), 5432 },
        { nameof(CockroachDbDialect), 26257 },
        { nameof(YugabyteDbDialect), 5433 },
        { nameof(SpannerDialect), null },
        { nameof(MySqlDialect), 3306 },
        { nameof(MariaDbDialect), 3306 },
        { nameof(TiDbDialect), 4000 },
        { nameof(SqlServerDialect), 1433 },
        { nameof(SqliteDialect), null }
    };

    [Theory]
    [MemberData(nameof(Ports))]
    public void EachDialect_ReportsItsServersDefaultPort(string dialectName, int? expected)
    {
        var logger = NullLogger.Instance;
        SqlDialect dialect = dialectName switch
        {
            nameof(PostgreSqlDialect) => new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), logger),
            nameof(CockroachDbDialect) => new CockroachDbDialect(new fakeDbFactory(SupportedDatabase.CockroachDb), logger),
            nameof(YugabyteDbDialect) => new YugabyteDbDialect(new fakeDbFactory(SupportedDatabase.YugabyteDb), logger),
            nameof(SpannerDialect) => new SpannerDialect(new fakeDbFactory(SupportedDatabase.Spanner), logger),
            nameof(MySqlDialect) => new MySqlDialect(new fakeDbFactory(SupportedDatabase.MySql), logger),
            nameof(MariaDbDialect) => new MariaDbDialect(new fakeDbFactory(SupportedDatabase.MariaDb), logger),
            nameof(TiDbDialect) => new TiDbDialect(new fakeDbFactory(SupportedDatabase.TiDb), logger),
            nameof(SqlServerDialect) => new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), logger),
            nameof(SqliteDialect) => new SqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite), logger),
            _ => throw new ArgumentOutOfRangeException(nameof(dialectName))
        };

        Assert.Equal(expected, dialect.DefaultServerPort);
    }
}
