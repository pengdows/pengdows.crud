using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// DisposeCount counts a caller's disposals. DisposeAsync counted itself and then counted again in
/// the Dispose(true) that DbConnection.DisposeAsync's base implementation calls, so one async
/// disposal reported 2 and a test could not tell it from a real double dispose.
/// </summary>
public sealed class FakeDbConnectionDisposeCountTests
{
    [Fact]
    public async Task DisposeAsync_CountsOnce()
    {
        var connection = new fakeDbConnection();

        await connection.DisposeAsync();

        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public void Dispose_CountsOnce()
    {
        var connection = new fakeDbConnection();

        connection.Dispose();

        Assert.Equal(1, connection.DisposeCount);
    }
}

/// <summary>
/// Found 2026-09-29: fakeDbFactory.EnqueueReaderResult created its connection through
/// CreateConnection (recording it) and queued it back, so the context's later CreateConnection
/// recorded the same instance again and CreatedConnections listed it twice.
/// </summary>
public sealed class FakeDbFactoryCreatedConnectionsTests
{
    [Fact]
    public void EnqueueReaderResult_ThenCreateConnection_ListsTheConnectionOnce()
    {
        var factory = new pengdows.crud.fakeDb.fakeDbFactory(pengdows.crud.enums.SupportedDatabase.Sqlite);
        factory.EnqueueReaderResult(new[] { new System.Collections.Generic.Dictionary<string, object> { ["v"] = 1 } });

        var connection = factory.CreateConnection();

        var created = Xunit.Assert.Single(factory.CreatedConnections);
        Xunit.Assert.Same(connection, created);
    }
}

/// <summary>
/// Found 2026-09-29: version queries (SELECT VERSION() and the like) always returned fakeDb's canned
/// version, so a test could not emulate e.g. MySQL 5.7 for version-gated dialect behavior.
/// </summary>
public sealed class FakeDbFactoryServerVersionTests
{
    [Fact]
    public async System.Threading.Tasks.Task ServerVersion_IsReturnedByTheProductVersionQuery()
    {
        var factory = new pengdows.crud.fakeDb.fakeDbFactory(pengdows.crud.enums.SupportedDatabase.MySql)
        {
            ServerVersion = "5.7.44"
        };
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT VERSION()";

        Xunit.Assert.Equal("5.7.44", await command.ExecuteScalarAsync());
        Xunit.Assert.Equal("5.7.44", connection.ServerVersion);
    }
}
