using Microsoft.EntityFrameworkCore;
using pengdows.crud.enums;

namespace pengdows.stormgate.Tests;

public sealed class EntityFrameworkCoreFakeDbIntegrationTests
{
    [Fact]
    public async Task GatedFakeDbConnection_SuppliesEntityFrameworkCoreQueryResultsWithoutADatabase()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.EnqueueReaderResult(
        [
            new Dictionary<string, object> { ["Value"] = 42 }
        ]);

        await using var gate = StormGate.Create(
            factory,
            "Data Source=not-a-real-database.db",
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(100));
        await using var connection = await gate.OpenAsync();

        var options = new DbContextOptionsBuilder<FakeDbContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .Options;
        await using var db = new FakeDbContext(options);

        var values = await db.Database
            .SqlQueryRaw<int>("SELECT 42 AS Value")
            .ToListAsync();

        Assert.Equal([42], values);
    }

    private sealed class FakeDbContext(DbContextOptions<FakeDbContext> options) : DbContext(options);
}
