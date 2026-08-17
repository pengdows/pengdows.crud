using Dapper;
using Microsoft.Data.Sqlite;

namespace pengdows.stormgate.Tests;

public sealed class DapperIntegrationTests
{
    [Fact]
    public async Task GatedConnection_SupportsDapperCommandsQueriesAndTransactions_AndReleasesItsPermit()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"stormgate-dapper-{Guid.NewGuid():N}.db");

        try
        {
            await using var gate = StormGate.Create(
                SqliteFactory.Instance,
                $"Data Source={databasePath}",
                maxConcurrentOpens: 1,
                acquireTimeout: TimeSpan.FromMilliseconds(100));

            await using (var connection = await gate.OpenAsync())
            {
                await connection.ExecuteAsync("CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");

                await using var transaction = await connection.BeginTransactionAsync();
                await connection.ExecuteAsync(
                    "INSERT INTO customers (name) VALUES (@Name)",
                    new { Name = "Ada" },
                    transaction);
                await transaction.CommitAsync();

                var customer = await connection.QuerySingleAsync<Customer>(
                    "SELECT id AS Id, name AS Name FROM customers WHERE name = @Name",
                    new { Name = "Ada" });

                Assert.Equal(1, customer.Id);
                Assert.Equal("Ada", customer.Name);
            }

            await using var nextConnection = await gate.OpenAsync();
            Assert.Equal(ConnectionState.Open, nextConnection.State);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private sealed class Customer
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }
}
