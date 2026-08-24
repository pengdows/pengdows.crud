using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using pengdows.stormgate;

namespace pengdows.stormgate.EntityFrameworkCore.Tests;

// Three further proofs that StormGate's admission control keeps working under real EF Core
// behaviors beyond plain sequential open/close: cancellation of an in-flight query (not just a
// connection open), a real EF Core execution strategy retrying a transient failure, and the
// interceptor composing with EF Core's own pooled-context DI entry points — all against fakeDb,
// zero real database engine involved.
public sealed class StormGateEfCoreResilienceTests
{
    // Extends PermitIsReleased_WhenReaderFailsMidEnumeration (in
    // StormGateConnectionInterceptorFakeDbTests) from "a canned exception fails the read" to "the
    // caller's own real CancellationToken is cancelled mid-stream" — a different failure trigger
    // exercising the same connection-cleanup invariant.
    [Fact]
    public async Task PermitIsReleased_WhenQueryIsCanceled_DuringActiveRowStreaming_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        var connection = (fakeDbConnection)factory.CreateConnection()!;
        using var cts = new CancellationTokenSource();
        connection.EnqueueReaderResult(
            new[]
            {
                new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "Ada" },
                new Dictionary<string, object?> { ["Id"] = 2, ["Name"] = "Grace" }
            },
            cancelAfterRowCount: 1,
            cts);

        var options = new DbContextOptionsBuilder<BlogContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .UseStormGate(interceptor)
            .Options;

        await using (var context = new BlogContext(options))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => context.Blogs.ToListAsync(cts.Token));
        }

        // Would time out if the canceled query's connection never closed, leaking its permit.
        await using var nextContext = CreateProbeContext(factory, interceptor);
        await nextContext.Database.OpenConnectionAsync();
        await nextContext.Database.CloseConnectionAsync();
    }

    // SQLite's EF Core provider executes a raw ExecuteSqlRawAsync statement via
    // ExecuteNonQueryAsync directly (unlike SaveChanges' modification-command batching, which
    // reads rows-affected back through a reader — see
    // EntityFrameworkCoreFakeDbErrorTranslationTests for that discovery). That keeps this test
    // focused purely on execution-strategy retry behavior via fakeDb's queued transient failures,
    // without also depending on SaveChanges' reader-based batching quirk.
    [Fact]
    public async Task ExecuteSqlRawAsync_RetriesTransientFailure_ViaRealExecutionStrategy_PermitNotLeakedAcrossRetries_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        var connection = (fakeDbConnection)factory.CreateConnection()!;
        connection.EnqueueTransientNonQueryFailures(
            new InvalidOperationException("transient #1"),
            new InvalidOperationException("transient #2"));

        var options = new DbContextOptionsBuilder<RetryTestDbContext>()
            .UseSqlite(
                connection,
                contextOwnsConnection: false,
                sqlite => sqlite.ExecutionStrategy(
                    deps => new AlwaysRetryExecutionStrategy(deps, maxRetryCount: 5, maxRetryDelay: TimeSpan.Zero)))
            .UseStormGate(interceptor)
            .Options;

        await using var db = new RetryTestDbContext(options);

        // ExecuteSqlRawAsync does not itself wrap execution in the configured IExecutionStrategy —
        // CreateExecutionStrategy().ExecuteAsync(...) is EF Core's own documented way to run an
        // operation under retry, and is what actually invokes AlwaysRetryExecutionStrategy here.
        var strategy = db.Database.CreateExecutionStrategy();
        var affected = await strategy.ExecuteAsync(
            () => db.Database.ExecuteSqlRawAsync("UPDATE \"Customers\" SET \"Name\" = 'Grace' WHERE \"Id\" = 1"));

        // fakeDb's default ExecuteNonQuery fallback (no queued NonQueryResults) returns 1 —
        // proof the third attempt actually ran the real statement rather than short-circuiting.
        Assert.Equal(1, affected);

        // Makes the "two failures were consumed before success" inference explicit: exactly 3
        // commands were created on this connection (a failing attempt still creates its DbCommand
        // before throwing — it just never reaches the ExecutedNonQueryCommands recording step,
        // which only runs on success), one per execution-strategy attempt.
        Assert.Equal(3, connection.CreatedCommands.Count);

        // Would time out if a retried attempt's connection open/close cycle leaked a permit.
        await using var nextContext = CreateProbeContext(factory, interceptor);
        await nextContext.Database.OpenConnectionAsync();
        await nextContext.Database.CloseConnectionAsync();
    }

    // The interceptor's own doc comment claims it "composes with AddDbContextPool ... and
    // IDbContextFactory alike, because it fires on every physical connection open/close
    // regardless of how the owning DbContext instance was created or pooled" — nothing exercised
    // that claim until now.
    //
    // Note on scope: AddPooledDbContextFactory builds ONE DbContextOptions instance from its
    // configuration delegate and reuses it for every pooled DbContext it constructs — an explicit
    // DbConnection instance embedded via UseSqlite(connection, ...) is therefore the SAME shared
    // connection object across every pooled instance, not a fresh one per instance (this was
    // verified empirically: an earlier version of this test tried to give each pooled instance
    // its own fakeDbConnection and found context2's open silently succeeded on the same already-
    // open connection instead of contending for a second permit). That means genuine *concurrent*
    // pooled connections aren't something this fakeDb-via-explicit-connection setup can exercise
    // at all — pooling a real per-instance ADO.NET connection would need a connection-string-only
    // configuration resolvable by a real provider, which fakeDb (deliberately) isn't registered
    // as. What CAN be proven, and is proven below, is narrower but still real: the interceptor
    // keeps working correctly — acquiring and releasing its permit — when the DbContext was
    // resolved via DI's AddPooledDbContextFactory/IDbContextFactory<T> rather than constructed
    // directly, across a full rent → open → close → rent-again cycle.
    [Fact]
    public async Task StormGate_ComposesWithPooledDbContextFactory_AdmissionControlStillApplies_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<PooledTestDbContext>(options =>
            options.UseSqlite(connection, contextOwnsConnection: false).UseStormGate(interceptor));

        await using var provider = services.BuildServiceProvider();
        var contextFactory = provider.GetRequiredService<IDbContextFactory<PooledTestDbContext>>();

        // Rent a pooled context, open+close through it — proves DI/pooling wiring for
        // UseStormGate doesn't break admission control on a simple round trip.
        await using (var rented1 = await contextFactory.CreateDbContextAsync())
        {
            await rented1.Database.OpenConnectionAsync();
            await rented1.Database.CloseConnectionAsync();
        }

        // A second rental (opaque to the test whether the pool recycled the first instance or
        // built a new one) is still gated by the SAME interceptor instance: opening it and then
        // trying to open an unrelated, manually-constructed context sharing that interceptor must
        // saturate the single permit.
        await using var rented2 = await contextFactory.CreateDbContextAsync();
        await rented2.Database.OpenConnectionAsync();

        await using var probe = CreateProbeContext(factory, interceptor);
        await Assert.ThrowsAsync<TimeoutException>(() => probe.Database.OpenConnectionAsync());

        await rented2.Database.CloseConnectionAsync();
    }

    private static RetryTestDbContext CreateProbeContext(fakeDbFactory factory, StormGateConnectionInterceptor interceptor)
    {
        var connection = factory.CreateConnection()!;
        var options = new DbContextOptionsBuilder<RetryTestDbContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .UseStormGate(interceptor)
            .Options;
        return new RetryTestDbContext(options);
    }

    private sealed class BlogContext(DbContextOptions<BlogContext> options) : DbContext(options)
    {
        public DbSet<Blog> Blogs => Set<Blog>();
    }

    private sealed class Blog
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class RetryTestDbContext(DbContextOptions<RetryTestDbContext> options) : DbContext(options);

    private sealed class PooledTestDbContext(DbContextOptions<PooledTestDbContext> options) : DbContext(options);

    private sealed class AlwaysRetryExecutionStrategy : ExecutionStrategy
    {
        public AlwaysRetryExecutionStrategy(ExecutionStrategyDependencies dependencies, int maxRetryCount, TimeSpan maxRetryDelay)
            : base(dependencies, maxRetryCount, maxRetryDelay)
        {
        }

        protected override bool ShouldRetryOn(Exception exception) => true;
    }
}
