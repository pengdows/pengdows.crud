using Microsoft.EntityFrameworkCore;
using pengdows.crud.enums;

namespace pengdows.stormgate.Tests;

// Proves EF Core's OWN error-translation layer — independent of pengdows.crud's
// ISqlDialect.AnalyzeException system entirely — reacts correctly to conditions fakeDb can
// simulate without a real database engine: a zero-rows-affected UPDATE (optimistic concurrency)
// and an arbitrary provider failure during SaveChanges (which EF always wraps in DbUpdateException).
public sealed class EntityFrameworkCoreFakeDbErrorTranslationTests
{
    [Fact]
    public async Task SaveChangesAsync_Throws_DbUpdateConcurrencyException_WhenZeroRowsAffected_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        await using var gate = StormGate.Create(
            factory,
            "Data Source=not-a-real-database.db",
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(100));
        await using var connection = await gate.OpenAsync();
        var fake = factory.CreatedConnections[^1];

        var options = new DbContextOptionsBuilder<CustomerContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .Options;
        await using var db = new CustomerContext(options);

        var customer = new Customer { Id = 1, Name = "Ada", IsActive = true };
        db.Attach(customer);
        customer.Name = "Grace";

        // The row EF expects to update is already gone (or was concurrently modified). SQLite's
        // SaveChanges reads modification results back via ExecuteReaderAsync ("SELECT changes()"),
        // not ExecuteNonQueryAsync (see the sibling test below) — an empty reader result set is
        // what actually simulates "0 rows affected" here. An EnqueueNonQueryResult(0) call would
        // be silently unconsumed dead setup; the assertion below locks that in so a future
        // EF/SQLite provider change back to the non-query path couldn't silently invalidate what
        // this test claims to prove without failing loudly instead.
        fake.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());

        Assert.Empty(fake.ExecutedNonQueryCommands);
    }

    [Fact]
    public async Task SaveChangesAsync_Throws_DbUpdateException_WrappingTheProviderFailure_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        await using var gate = StormGate.Create(
            factory,
            "Data Source=not-a-real-database.db",
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(100));
        await using var connection = await gate.OpenAsync();
        var fake = factory.CreatedConnections[^1];

        var options = new DbContextOptionsBuilder<CustomerContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .Options;
        await using var db = new CustomerContext(options);

        var customer = new Customer { Id = 1, Name = "Ada", IsActive = true };
        db.Attach(customer);
        customer.Name = "Grace";

        // SQLite's EF Core provider executes SaveChanges' UPDATE via ExecuteReaderAsync (reading
        // back "SELECT changes()" on the same command), not ExecuteNonQueryAsync — so the
        // failure must be injected on the reader path. failAfterRowCount: 0 throws on the very
        // first read attempt, before any row is returned.
        var providerFailure = new InvalidOperationException("simulated UNIQUE constraint failed: Customers.Name");
        fake.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>(), failAfterRowCount: 0, providerFailure);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Same(providerFailure, thrown.InnerException);
        Assert.Empty(fake.ExecutedNonQueryCommands);
    }

    private sealed class CustomerContext(DbContextOptions<CustomerContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();
    }

    private sealed class Customer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}
