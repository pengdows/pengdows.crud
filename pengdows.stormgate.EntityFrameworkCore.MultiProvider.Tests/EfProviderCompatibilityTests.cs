namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

// Answers the question that started this project directly, per provider: does UseXxx(connection,
// contextOwnsConnection: false) accept an arbitrary fakeDbConnection at all (rather than requiring
// its own concrete connection type), and does StormGate's DbConnectionInterceptor-based admission
// control — which fires on EF Core's own connection lifecycle events, not on anything
// provider-specific — work identically regardless of which provider is driving the connection?
// No real database engine of any kind is involved for any provider tested here.
public sealed class EfProviderCompatibilityTests
{
    [Theory]
    [MemberData(nameof(EfProviders.All), MemberType = typeof(EfProviders))]
    public async Task AcceptsExternallySuppliedFakeDbConnection_AndStormGateAdmissionControlWorks(
        SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var interceptor = new StormGateConnectionInterceptor(
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(150));

        await using var context1 = CreateContext(database, factory, interceptor);
        await using var context2 = CreateContext(database, factory, interceptor);

        await context1.Database.OpenConnectionAsync();

        // Some providers' default execution strategy (e.g. SQL Server's) reclassifies a
        // TimeoutException raised during connection open as transient-looking and wraps it in
        // its own InvalidOperationException with an "enable EnableRetryOnFailure" suggestion,
        // rather than propagating it raw the way SQLite does — so the exact exception TYPE the
        // caller sees is provider-dependent, but the interceptor's saturation TimeoutException
        // must still be the traceable root cause regardless of how a given provider wraps it.
        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => context2.Database.OpenConnectionAsync());
        var saturation = FindStormGateSaturationTimeout(thrown);
        Assert.NotNull(saturation);

        await context1.Database.CloseConnectionAsync();

        await context2.Database.OpenConnectionAsync();
        await context2.Database.CloseConnectionAsync();
    }

    private static TimeoutException? FindStormGateSaturationTimeout(Exception? exception)
    {
        while (exception != null)
        {
            if (exception is TimeoutException { Message: var message } timeout
                && message.Contains("storm gate", StringComparison.OrdinalIgnoreCase))
            {
                return timeout;
            }

            exception = exception.InnerException;
        }

        return null;
    }

    private static ProbeDbContext CreateContext(
        SupportedDatabase database,
        fakeDbFactory factory,
        StormGateConnectionInterceptor interceptor)
    {
        var connection = factory.CreateConnection()!;
        var builder = new DbContextOptionsBuilder<ProbeDbContext>();
        EfProviders.Configure(database, builder, connection);
        builder.UseStormGate(interceptor);
        return new ProbeDbContext(builder.Options);
    }
}

public sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options);
