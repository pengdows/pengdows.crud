using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerStoredProcedureWrappingTests
{
    [Fact]
    public async Task ExecuteReaderAsync_WithStoredProcedure_UsesWrappedTextCommand()
    {
        var typeMap = new TypeMapRegistry();
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);

        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory, typeMap);
        await using var container = context.CreateSqlContainer("my_proc");
        container.AddParameterWithValue("param1", DbType.Int32, 5);

        connection.EnqueueReaderResult(new List<Dictionary<string, object?>>
        {
            new() { ["Value"] = 1 }
        });

        var reader = await container.ExecuteReaderAsync(CommandType.StoredProcedure);
        var command = connection.LastCreatedCommand;

        Assert.NotNull(command);
        Assert.Equal(CommandType.Text, command!.CommandType);
        Assert.Single(connection.ExecutedReaderTexts);
        Assert.Equal("EXEC [my_proc] @param1", connection.ExecutedReaderTexts[0]);

        await reader.DisposeAsync();
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithStoredProcedure_ThrowsWhenNotSupported()
    {
        var typeMap = new TypeMapRegistry();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);

        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, typeMap);
        await using var container = context.CreateSqlContainer("my_proc");

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => container.ExecuteReaderAsync(CommandType.StoredProcedure));

        Assert.Contains("Stored procedures are not supported", exception.Message);
    }
}
