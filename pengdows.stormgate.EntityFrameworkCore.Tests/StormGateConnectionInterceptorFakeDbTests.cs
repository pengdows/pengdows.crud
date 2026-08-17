using Microsoft.EntityFrameworkCore;

namespace pengdows.stormgate.EntityFrameworkCore.Tests;

// Proves the interceptor's throttle/timeout/release logic against a fakeDb-backed connection —
// zero real database engine involved, not even in-process SQLite. DbConnectionInterceptor
// hooks fire on Open/Close for ANY DbConnection EF Core is handed, real or fake, so the exact
// same admission-control behavior verified against real SQLite in
// StormGateConnectionInterceptorTests can be proven entirely offline.
public sealed class StormGateConnectionInterceptorFakeDbTests
{
    [Fact]
    public async Task SecondContext_TimesOut_WhileFirstConnectionIsStillOpen_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var interceptor = new StormGateConnectionInterceptor(
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(150));

        await using var context1 = CreateContext(factory, interceptor);
        await using var context2 = CreateContext(factory, interceptor);

        await context1.Database.OpenConnectionAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => context2.Database.OpenConnectionAsync());

        await context1.Database.CloseConnectionAsync();

        // The permit released on close must be available to the next opener.
        await context2.Database.OpenConnectionAsync();
        await context2.Database.CloseConnectionAsync();
    }

    // fakeDbConnection.BreakConnection(skipFirst: true) marks the connection to fail on its
    // next physical Open() without changing state up front — the fake-database equivalent of
    // forcing a provider-level open failure, letting this exercise ConnectionFailed(Async)
    // without needing a real broken connection string.
    [Fact]
    public async Task PermitIsReleased_WhenPhysicalOpenFails_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var interceptor = new StormGateConnectionInterceptor(
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(150));

        var failingConnection = (fakeDbConnection)factory.CreateConnection()!;
        failingConnection.BreakConnection(skipFirst: true);

        var failingOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(failingConnection, contextOwnsConnection: false)
            .UseStormGate(interceptor)
            .Options;

        await using (var failingContext = new TestDbContext(failingOptions))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => failingContext.Database.OpenConnectionAsync());
        }

        // Would time out if the failed open above had leaked its permit.
        await using var context = CreateContext(factory, interceptor);
        await context.Database.OpenConnectionAsync();
        await context.Database.CloseConnectionAsync();
    }

    private static TestDbContext CreateContext(fakeDbFactory factory, StormGateConnectionInterceptor interceptor)
    {
        var connection = factory.CreateConnection()!;
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .UseStormGate(interceptor)
            .Options;
        return new TestDbContext(options);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
}
