using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace pengdows.stormgate.Tests;

public sealed class EntityFrameworkCoreIntegrationTests
{
    [Fact]
    public async Task GatedConnection_CanBeUsedByEntityFrameworkCore_AndReleasesItsPermit()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"stormgate-ef-{Guid.NewGuid():N}.db");

        try
        {
            await using var gate = StormGate.Create(
                SqliteFactory.Instance,
                $"Data Source={databasePath}",
                maxConcurrentOpens: 1,
                acquireTimeout: TimeSpan.FromMilliseconds(100));

            await using (var connection = await gate.OpenAsync())
            {
                var options = new DbContextOptionsBuilder<CustomerContext>()
                    .UseSqlite(connection, contextOwnsConnection: false)
                    .Options;

                await using var db = new CustomerContext(options);
                await db.Database.EnsureCreatedAsync();
                db.Customers.Add(new Customer { Name = "Ada" });
                await db.SaveChangesAsync();

                Assert.Equal("Ada", (await db.Customers.SingleAsync(customer => customer.Id == 1)).Name);

                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT 1";
                    command.CommandTimeout = 5;
                    command.CommandType = CommandType.Text;
                    command.DesignTimeVisible = false;
                    command.UpdatedRowSource = UpdateRowSource.None;
                    command.Parameters.Add(command.CreateParameter());
                    command.Parameters.Clear();
                    command.Prepare();
                    Assert.Equal(1L, command.ExecuteScalar());
                    Assert.Equal(1L, await command.ExecuteScalarAsync());
                    command.Cancel();
                }

                await using (var transaction = await connection.BeginTransactionAsync())
                {
                    await transaction.CommitAsync();
                }

                await using (var transaction = await connection.BeginTransactionAsync())
                {
                    await transaction.RollbackAsync();
                }
            }

            await using var nextConnection = await gate.OpenAsync();
            Assert.Equal(ConnectionState.Open, nextConnection.State);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private sealed class CustomerContext(DbContextOptions<CustomerContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();
    }

    private sealed class Customer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
