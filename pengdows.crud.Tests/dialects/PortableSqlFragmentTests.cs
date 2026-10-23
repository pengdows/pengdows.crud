using System;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// SQL fragments a caller writing its own SQL needs per database, so it can ask the dialect instead of
/// switching on IDatabaseContext.Product (pengdows.hangfire did, and its switches missed the Aurora
/// labels): the server's current UTC time, the clause a table-less SELECT needs, and how to claim rows
/// while skipping rows other sessions have locked. Values are the ones pengdows.hangfire ran live on
/// each database; skip-locked is reported only where it was verified (SQL Server, PostgreSQL).
/// </summary>
public sealed class PortableSqlFragmentTests
{
    private static ISqlDialect Dialect(SupportedDatabase database) =>
        SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database), NullLogger<SqlDialect>.Instance);

    [Theory]
    [InlineData(SupportedDatabase.SqlServer, "SYSUTCDATETIME()")]
    [InlineData(SupportedDatabase.PostgreSql, "CURRENT_TIMESTAMP AT TIME ZONE 'UTC'")]
    [InlineData(SupportedDatabase.AuroraPostgreSql, "CURRENT_TIMESTAMP AT TIME ZONE 'UTC'")]
    [InlineData(SupportedDatabase.MySql, "UTC_TIMESTAMP(6)")]
    [InlineData(SupportedDatabase.AuroraMySql, "UTC_TIMESTAMP(6)")]
    [InlineData(SupportedDatabase.MariaDb, "UTC_TIMESTAMP(6)")]
    [InlineData(SupportedDatabase.Oracle, "SYS_EXTRACT_UTC(SYSTIMESTAMP)")]
    [InlineData(SupportedDatabase.Sqlite, "STRFTIME('%Y-%m-%dT%H:%M:%fZ', 'now')")]
    [InlineData(SupportedDatabase.Firebird, "CAST(CURRENT_TIMESTAMP AT TIME ZONE 'UTC' AS TIMESTAMP)")]
    public void CurrentUtcTimestampExpression_IsTheDatabases(SupportedDatabase database, string expected)
    {
        Assert.Equal(expected, Dialect(database).CurrentUtcTimestampExpression);
    }

    [Theory]
    [InlineData(SupportedDatabase.Oracle, " FROM DUAL")]
    [InlineData(SupportedDatabase.Firebird, " FROM RDB$DATABASE")]
    [InlineData(SupportedDatabase.SqlServer, "")]
    [InlineData(SupportedDatabase.PostgreSql, "")]
    [InlineData(SupportedDatabase.MySql, "")]
    [InlineData(SupportedDatabase.Sqlite, "")]
    public void SelectWithoutTableSuffix_IsTheDatabases(SupportedDatabase database, string expected)
    {
        Assert.Equal(expected, Dialect(database).SelectWithoutTableSuffix);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer, " WITH (UPDLOCK, READPAST, ROWLOCK)", null)]
    [InlineData(SupportedDatabase.PostgreSql, null, " FOR UPDATE SKIP LOCKED")]
    [InlineData(SupportedDatabase.AuroraPostgreSql, null, " FOR UPDATE SKIP LOCKED")]
    [InlineData(SupportedDatabase.CockroachDb, null, null)]
    [InlineData(SupportedDatabase.YugabyteDb, null, null)]
    [InlineData(SupportedDatabase.MySql, null, null)]
    [InlineData(SupportedDatabase.Oracle, null, null)]
    [InlineData(SupportedDatabase.Sqlite, null, null)]
    public void SkipLockedFragments_AreReportedOnlyWhereVerified(SupportedDatabase database, string? tableHint,
        string? clause)
    {
        var dialect = Dialect(database);

        Assert.Equal(tableHint, dialect.SkipLockedTableHint);
        Assert.Equal(clause, dialect.SkipLockedClause);
    }

    // Added after 2.0.5: an ISqlDialect implemented against 2.0.5 keeps working with these defaults.
    [Fact]
    public void DefaultImplementations_AreThePortableFallbacks()
    {
        var dialect = new Mock<ISqlDialect> { CallBase = true }.Object;

        Assert.Equal("CURRENT_TIMESTAMP", dialect.CurrentUtcTimestampExpression);
        Assert.Equal(string.Empty, dialect.SelectWithoutTableSuffix);
        Assert.Null(dialect.SkipLockedTableHint);
        Assert.Null(dialect.SkipLockedClause);
    }
}
