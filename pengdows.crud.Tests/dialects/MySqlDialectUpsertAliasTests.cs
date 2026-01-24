using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class MySqlDialectUpsertAliasTests
{
    [Fact]
    public async Task UpsertIncomingColumn_UsesAlias_OnModernMySql()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        using var connection = new fakeDbConnection();
        connection.EmulatedProduct = SupportedDatabase.MySql;
        connection.SetServerVersion("8.0.33");
        connection.SetScalarResultForCommand("SELECT VERSION()", "8.0.33");

        using var tracked = new TrackedConnection(connection);
        tracked.Open();

        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(tracked);

        Assert.Equal("incoming", dialect.UpsertIncomingAlias);
        Assert.Equal("\"incoming\".\"col\"", dialect.UpsertIncomingColumn("col"));
    }

    [Fact]
    public async Task UpsertIncomingColumn_FallsBack_OnLegacyMySql()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        using var connection = new fakeDbConnection();
        connection.EmulatedProduct = SupportedDatabase.MySql;
        connection.SetServerVersion("8.0.19");
        connection.SetScalarResultForCommand("SELECT VERSION()", "8.0.19");

        using var tracked = new TrackedConnection(connection);
        tracked.Open();

        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(tracked);

        Assert.Null(dialect.UpsertIncomingAlias);
        Assert.Equal("VALUES(\"col\")", dialect.UpsertIncomingColumn("col"));
    }
}
