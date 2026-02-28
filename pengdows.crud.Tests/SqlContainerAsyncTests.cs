using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerAsyncTests
{
    [Fact]
    public async Task ExecuteScalarRequiredAsync_ReturnsValue()
    {
        var connection = new fakeDbConnection();
        connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "value", 42 } }
        });
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.Connections.Add(connection);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.SingleConnection
        };

        using var context = new DatabaseContext(config, factory);

        // Initialization probes consume queued results, so re-prime the connection for the actual command.
        connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "value", 42 } }
        });

        using var container = context.CreateSqlContainer("SELECT 1");

        var result = await container.ExecuteScalarRequiredAsync<int>();

        Assert.Equal(42, result);
    }
}