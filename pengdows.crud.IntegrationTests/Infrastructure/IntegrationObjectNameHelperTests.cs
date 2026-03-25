using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace pengdows.crud.IntegrationTests.Infrastructure;

public sealed class IntegrationObjectNameHelperTests
{
    [Fact]
    public void Table_QualifiesSnowflakeNames_WithDatabaseAndSchema()
    {
        using var context = new DatabaseContext(
            "account=test;user=tester;db=TEST_DB;schema=PUBLIC",
            new fakeDbFactory(SupportedDatabase.Snowflake));

        var table = IntegrationObjectNameHelper.Table(context, "test_table");

        Assert.Equal("\"TEST_DB\".\"PUBLIC\".\"test_table\"", table);
    }

    [Fact]
    public void Table_QualifiesPostgreSqlNames_WithSearchPathSchema()
    {
        using var context = new DatabaseContext(
            "Host=localhost;Database=testdb;Search Path=app",
            new fakeDbFactory(SupportedDatabase.PostgreSql));

        var table = IntegrationObjectNameHelper.Table(context, "test_table");

        Assert.Equal("\"app\".\"test_table\"", table);
    }

    [Fact]
    public void Table_QualifiesMySqlNames_WithDatabase()
    {
        using var context = new DatabaseContext(
            "Server=localhost;Database=testdb;User=root;Password=test",
            new fakeDbFactory(SupportedDatabase.MySql));

        var table = IntegrationObjectNameHelper.Table(context, "test_table");

        Assert.Contains("testdb", table, StringComparison.Ordinal);
        Assert.Contains("test_table", table, StringComparison.Ordinal);
        Assert.Contains(context.CompositeIdentifierSeparator, table, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_QualifiesSqlServerNames_WithDefaultDboSchema()
    {
        using var context = new DatabaseContext(
            "Server=localhost;Initial Catalog=testdb;User Id=sa;Password=test",
            new fakeDbFactory(SupportedDatabase.SqlServer));

        var table = IntegrationObjectNameHelper.Table(context, "test_table");

        Assert.Contains("dbo", table, StringComparison.Ordinal);
        Assert.Contains("test_table", table, StringComparison.Ordinal);
        Assert.Contains(context.CompositeIdentifierSeparator, table, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_LeavesSqliteNamesUnqualified()
    {
        using var context = new DatabaseContext(
            "Data Source=:memory:",
            new fakeDbFactory(SupportedDatabase.Sqlite));

        var table = IntegrationObjectNameHelper.Table(context, "test_table");

        Assert.Equal("\"test_table\"", table);
    }

    [Fact]
    public void Table_LeavesOracleNamesUnqualified_BecauseConnectionStringIsRedacted()
    {
        // DatabaseContext.ConnectionString returns a redacted string where User Id=REDACTED.
        // IntegrationObjectNameHelper must not use User Id as Oracle schema prefix, since
        // Oracle tables are created in the connected user's schema by default.
        using var context = new DatabaseContext(
            "User Id=system;Password=mysecurepassword;Data Source=localhost:1521/FREEPDB1",
            new fakeDbFactory(SupportedDatabase.Oracle));

        var table = IntegrationObjectNameHelper.Table(context, "test_table");

        Assert.DoesNotContain("REDACTED", table, StringComparison.Ordinal);
        Assert.DoesNotContain("SYSTEM", table, StringComparison.Ordinal);
        Assert.Equal("\"test_table\"", table);
    }
}
