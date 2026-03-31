using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Verifies that each dialect self-reports whether it supports read-only transactions.
/// This drives the integration-test gate via <see cref="ISqlDialect.SupportsReadOnlyTransactions"/>
/// instead of a hardcoded provider switch.
/// </summary>
public class SupportsReadOnlyTransactionsTests
{
    private static ISqlDialect Create(SupportedDatabase db) =>
        SqlDialectFactory.CreateDialectForType(db, new fakeDbFactory(db), NullLogger<SqlDialect>.Instance);

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    [InlineData(SupportedDatabase.TiDb)]
    public void SupportsReadOnlyTransactions_IsTrue(SupportedDatabase db)
    {
        Assert.True(Create(db).SupportsReadOnlyTransactions);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.DuckDB)]
    [InlineData(SupportedDatabase.Snowflake)]
    public void SupportsReadOnlyTransactions_IsFalse(SupportedDatabase db)
    {
        Assert.False(Create(db).SupportsReadOnlyTransactions);
    }
}
