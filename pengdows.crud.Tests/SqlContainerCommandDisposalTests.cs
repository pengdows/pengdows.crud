using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerCommandDisposalTests
{
    [Fact]
    public async Task ExecuteReaderAsync_DisposesCommandAfterReaderDisposed()
    {
        var typeMap = new TypeMapRegistry();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);

        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);
        await using var container = context.CreateSqlContainer("SELECT 1");

        connection.EnqueueReaderResult(new List<Dictionary<string, object?>>
        {
            new() { ["Value"] = 1 }
        });

        var reader = await container.ExecuteReaderAsync();
        var command = connection.LastCreatedCommand;

        Assert.NotNull(command);
        Assert.False(command!.WasDisposed);

        await reader.DisposeAsync();

        Assert.True(command.WasDisposed);
    }

    [Fact]
    public async Task ExecuteReaderAsync_DisposesCommandOnExecuteFailure()
    {
        var typeMap = new TypeMapRegistry();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);

        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);
        await using var container = context.CreateSqlContainer("SELECT 1");

        connection.SetCommandFailure("SELECT 1", new InvalidOperationException("fail"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => container.ExecuteReaderAsync());

        var command = connection.LastCreatedCommand;

        Assert.NotNull(command);
        Assert.True(command!.WasDisposed);
    }
}
