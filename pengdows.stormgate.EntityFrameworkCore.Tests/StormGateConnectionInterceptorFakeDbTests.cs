using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using pengdows.stormgate;

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
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

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
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

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

    [Fact]
    public async Task PermitIsReleased_WhenOpenConnectionBecomesBroken()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .UseStormGate(interceptor)
            .Options;

        await using (var context = new TestDbContext(options))
        {
            await context.Database.OpenConnectionAsync();
            connection.BreakConnection();
        }

        await using var nextContext = CreateContext(factory, interceptor);
        await nextContext.Database.OpenConnectionAsync();
        await nextContext.Database.CloseConnectionAsync();
    }

    // The queued reader answers 1 row successfully, then throws on the read attempt that would
    // fetch a second one — simulating a connection that drops mid-stream rather than one that
    // never opens. Proves EF Core's own connection-lifecycle cleanup (closing the connection it
    // implicitly opened for this query) still runs on the failure path, so the interceptor's
    // ConnectionClosed hook fires and the permit is not held forever by a query that blew up
    // partway through materialization.
    [Fact]
    public async Task PermitIsReleased_WhenReaderFailsMidEnumeration_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        var connection = (fakeDbConnection)factory.CreateConnection()!;
        var failure = new InvalidOperationException("simulated mid-stream I/O failure");
        connection.EnqueueReaderResult(
            new[] { new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "Ada" } },
            failAfterRowCount: 1,
            failure);

        var options = new DbContextOptionsBuilder<BlogContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .UseStormGate(interceptor)
            .Options;

        await using (var context = new BlogContext(options))
        {
            var caught = await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.Blogs.ToListAsync());
            Assert.Same(failure, caught);
        }

        // Would time out if the failed enumeration above leaked its permit.
        await using var nextContext = CreateContext(factory, interceptor);
        await nextContext.Database.OpenConnectionAsync();
        await nextContext.Database.CloseConnectionAsync();
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

    // Proves EF Core's real BeginTransactionAsync/CommitAsync/RollbackAsync actually reach the
    // fake transaction instance (via IDbContextTransaction.GetDbTransaction()) exactly once, and
    // that the connection a transaction implicitly opened is closed again on completion — so the
    // interceptor releases its permit rather than leaking it for the lifetime of the DbContext.
    [Fact]
    public async Task Transaction_CommitReachesFakeTransaction_AndReleasesPermitExactlyOnce_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        await using (var context = CreateContext(factory, interceptor))
        {
            await using var txn = await context.Database.BeginTransactionAsync();
            var fakeTxn = (fakeDbTransaction)txn.GetDbTransaction();

            Assert.Equal(0, fakeTxn.CommitCallCount);

            await txn.CommitAsync();

            Assert.Equal(1, fakeTxn.CommitCallCount);
            Assert.Equal(0, fakeTxn.RollbackCallCount);
        }

        // Would time out if the committed transaction's connection never closed, leaking its permit.
        await using var nextContext = CreateContext(factory, interceptor);
        await nextContext.Database.OpenConnectionAsync();
        await nextContext.Database.CloseConnectionAsync();
    }

    [Fact]
    public async Task Transaction_RollbackReachesFakeTransaction_AndReleasesPermit_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        await using (var context = CreateContext(factory, interceptor))
        {
            await using var txn = await context.Database.BeginTransactionAsync();
            var fakeTxn = (fakeDbTransaction)txn.GetDbTransaction();

            await txn.RollbackAsync();

            Assert.Equal(1, fakeTxn.RollbackCallCount);
            Assert.Equal(0, fakeTxn.CommitCallCount);
        }

        await using var nextContext = CreateContext(factory, interceptor);
        await nextContext.Database.OpenConnectionAsync();
        await nextContext.Database.CloseConnectionAsync();
    }

    // The two tests above only prove the success path. A commit/rollback that itself throws is a
    // different code path through EF's transaction disposal — worth proving separately that it
    // doesn't leave the connection open (and the permit held) forever.
    [Fact]
    public async Task Transaction_CommitFailure_StillReleasesPermit_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        var connection = (fakeDbConnection)factory.CreateConnection()!;
        var commitFailure = new InvalidOperationException("simulated commit failure");
        connection.SetTransactionCommitException(commitFailure);

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .UseStormGate(interceptor)
            .Options;

        await using (var context = new TestDbContext(options))
        {
            await using var txn = await context.Database.BeginTransactionAsync();
            var fakeTxn = (fakeDbTransaction)txn.GetDbTransaction();

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => txn.CommitAsync());
            Assert.Same(commitFailure, thrown);
            Assert.Equal(1, fakeTxn.CommitCallCount);
        }

        // Would time out if the failed commit's connection never closed, leaking its permit.
        await using var nextContext = CreateContext(factory, interceptor);
        await nextContext.Database.OpenConnectionAsync();
        await nextContext.Database.CloseConnectionAsync();
    }

    [Fact]
    public async Task Transaction_RollbackFailure_StillReleasesPermit_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        var connection = (fakeDbConnection)factory.CreateConnection()!;
        var rollbackFailure = new InvalidOperationException("simulated rollback failure");
        connection.SetTransactionRollbackException(rollbackFailure);

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .UseStormGate(interceptor)
            .Options;

        await using (var context = new TestDbContext(options))
        {
            await using var txn = await context.Database.BeginTransactionAsync();
            var fakeTxn = (fakeDbTransaction)txn.GetDbTransaction();

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => txn.RollbackAsync());
            Assert.Same(rollbackFailure, thrown);
            Assert.Equal(1, fakeTxn.RollbackCallCount);
        }

        await using var nextContext = CreateContext(factory, interceptor);
        await nextContext.Database.OpenConnectionAsync();
        await nextContext.Database.CloseConnectionAsync();
    }

    // Distinguishes connection lifetime from command lifetime: PermitIsReleased_WhenReaderFails...
    // and StormGateEfCoreResilienceTests' cancellation test both rely on EF's IMPLICIT
    // connection management auto-closing a connection it opened for a single operation. Here the
    // caller explicitly opens the connection first, so EF will NOT auto-close it just because one
    // query against it was canceled — the permit must remain held until the caller explicitly
    // closes the connection, regardless of what happens to any individual command run on it.
    [Fact]
    public async Task Permit_RemainsHeld_WhenOnlyTheQueryIsCanceled_OnAnExplicitlyOpenedConnection_NoRealDatabaseEngine()
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

        await using var context = new BlogContext(options);

        await context.Database.OpenConnectionAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.Blogs.ToListAsync(cts.Token));

        Assert.Equal(ConnectionState.Open, connection.State);

        // The permit is intentionally still held: the caller-opened connection is still open
        // regardless of the canceled query, so a concurrent open attempt must still saturate.
        await using var probe = CreateContext(factory, interceptor);
        await Assert.ThrowsAsync<TimeoutException>(() => probe.Database.OpenConnectionAsync());

        // Only explicitly closing the connection releases the permit.
        await context.Database.CloseConnectionAsync();
        await probe.Database.OpenConnectionAsync();
        await probe.Database.CloseConnectionAsync();
    }

    // The genuinely new proof over the sequential tests above: two opens are launched
    // concurrently and both are held paused mid-open (via SetOpenGate) before either completes.
    // If the semaphore secretly serialized opens instead of permitting maxConcurrentOpens at
    // once, the second task could only reach its own gate after the first was released — this
    // asserts both are paused *simultaneously*, then that a genuinely-concurrent third attempt
    // is the one that finds the gate saturated and times out. No Thread.Sleep/Task.Delay anywhere:
    // gate release is caller-controlled, so the sequencing is deterministic.
    [Fact]
    public async Task MaxConcurrentOpens_PermitsGenuinelyConcurrentOpens_NotSerialized_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 2, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        var connection1 = (fakeDbConnection)factory.CreateConnection()!;
        var connection2 = (fakeDbConnection)factory.CreateConnection()!;
        var connection3 = (fakeDbConnection)factory.CreateConnection()!;

        var gate1 = connection1.SetOpenGate();
        var gate2 = connection2.SetOpenGate();

        await using var context1 = CreateContext(connection1, interceptor);
        await using var context2 = CreateContext(connection2, interceptor);
        await using var context3 = CreateContext(connection3, interceptor);

        var openTask1 = context1.Database.OpenConnectionAsync();
        var openTask2 = context2.Database.OpenConnectionAsync();

        // Both permits are granted and both opens are simultaneously paused on their own gates.
        Assert.False(openTask1.IsCompleted);
        Assert.False(openTask2.IsCompleted);

        // A third concurrent attempt finds the gate saturated by the two paused opens above.
        await Assert.ThrowsAsync<TimeoutException>(() => context3.Database.OpenConnectionAsync());

        gate1.SetResult(true);
        await openTask1;
        await context1.Database.CloseConnectionAsync();

        // The permit context1 just released is now available to context3's retry.
        await context3.Database.OpenConnectionAsync();
        await context3.Database.CloseConnectionAsync();

        gate2.SetResult(true);
        await openTask2;
        await context2.Database.CloseConnectionAsync();
    }

    // An earlier version of this test used a connection whose OpenAsync returned an
    // already-canceled task, and was compiled only under NET10_0_OR_GREATER on the assumption
    // that verifying permit release on cancellation needed the .NET 10-only
    // ConnectionCanceled(Async) interceptor hooks. Running that exact scenario unguarded on
    // net8.0 revealed it passes there too — EF Core routes a canceled open through
    // ConnectionFailed(Async) ("EF Core fires ConnectionFailed(Async) for ANY exception during an
    // open attempt", per the comment on that override above), which this interceptor already
    // handled correctly before .NET 10 existed. But an already-canceled token also risks never
    // proving a permit was acquired at all — if EF short-circuited before ever calling
    // ConnectionOpeningAsync, "no leak" would be true only because nothing was ever admitted in
    // the first place. This version uses the deterministic open gate (see
    // MaxConcurrentOpens_PermitsGenuinelyConcurrentOpens... above) to prove the permit is
    // genuinely acquired and the physical open is genuinely paused mid-flight — observed via
    // openTask.IsCompleted being false — before a real, separately-triggered cancellation reaches
    // it, on both target frameworks.
    [Fact]
    public async Task PermitIsReleased_WhenOpenIsCanceled_AfterGenuinelyAcquiringThePermit_NoRealDatabaseEngine()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        var connection = (fakeDbConnection)factory.CreateConnection()!;
        connection.SetOpenGate(); // never released — the open is canceled instead of completing

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .UseStormGate(interceptor)
            .Options;

        using var cts = new CancellationTokenSource();

        await using (var context = new TestDbContext(options))
        {
            var openTask = context.Database.OpenConnectionAsync(cts.Token);

            // Proves the permit really was acquired and the physical open is genuinely paused —
            // not that cancellation short-circuited before any admission happened.
            Assert.False(openTask.IsCompleted);

            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => openTask);
        }

        // Would time out if the canceled open leaked its permit.
        await using var nextContext = CreateContext(factory, interceptor);
        await nextContext.Database.OpenConnectionAsync();
        await nextContext.Database.CloseConnectionAsync();
    }

    private static TestDbContext CreateContext(fakeDbFactory factory, StormGateConnectionInterceptor interceptor)
    {
        return CreateContext(factory.CreateConnection()!, interceptor);
    }

    private static TestDbContext CreateContext(DbConnection connection, StormGateConnectionInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .UseStormGate(interceptor)
            .Options;
        return new TestDbContext(options);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
}
