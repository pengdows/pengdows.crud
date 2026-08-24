using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using pengdows.stormgate;

namespace pengdows.stormgate.EntityFrameworkCore.Tests;

public sealed class StormGateConnectionInterceptorTests
{
    // Proves the actual "connection storm" claim end to end: a real EF Core connection held
    // open blocks a second real EF Core connection from opening once the gate is saturated,
    // and the second attempt fails fast with TimeoutException instead of queuing indefinitely
    // or overwhelming the provider. The StormGate instance is shared across both contexts —
    // exactly as it must be used in production (see README): a fresh StormGate per DbContext
    // would give each instance its own semaphore and throttle nothing.
    [Fact]
    public async Task SecondContext_TimesOut_WhileFirstConnectionIsStillOpen_ThenSucceedsAfterRelease()
    {
        var dbPath = TempDbPath();
        try
        {
            using var stormGate = CreateGate(dbPath, maxConcurrentOpens: 1, TimeSpan.FromMilliseconds(150));
            var interceptor = new StormGateConnectionInterceptor(stormGate);

            await using var context1 = CreateContext(dbPath, interceptor);
            await using var context2 = CreateContext(dbPath, interceptor);

            await context1.Database.OpenConnectionAsync();

            await Assert.ThrowsAsync<TimeoutException>(() => context2.Database.OpenConnectionAsync());

            await context1.Database.CloseConnectionAsync();

            // The permit StormGate released on close must be available to the next opener.
            await context2.Database.OpenConnectionAsync();
            await context2.Database.CloseConnectionAsync();
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    // If a connection open acquires a permit but the physical open then throws, the permit
    // must still be released — otherwise a single connection failure permanently shrinks the
    // gate's capacity.
    [Fact]
    public async Task PermitIsReleased_WhenPhysicalOpenFails()
    {
        var dbPath = TempDbPath();
        using var stormGate = CreateGate(dbPath, maxConcurrentOpens: 1, TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        var badConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(Path.GetTempPath(), $"stormgate-efcore-missing-{Guid.NewGuid():N}", "db.sqlite"),
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        var failingOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(badConnectionString)
            .UseStormGate(interceptor)
            .Options;

        await using (var failingContext = new TestDbContext(failingOptions))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => failingContext.Database.OpenConnectionAsync());
        }

        try
        {
            await using var context = CreateContext(dbPath, interceptor);

            // Would time out if the failed open above had leaked its permit.
            await context.Database.OpenConnectionAsync();
            await context.Database.CloseConnectionAsync();
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    // Same throttle-then-release proof, but through EF Core's synchronous connection APIs —
    // exercising ConnectionOpening/ConnectionClosed rather than the Async overrides.
    [Fact]
    public void SecondContext_TimesOut_WhileFirstConnectionIsStillOpen_Sync_ThenSucceedsAfterRelease()
    {
        var dbPath = TempDbPath();
        try
        {
            using var stormGate = CreateGate(dbPath, maxConcurrentOpens: 1, TimeSpan.FromMilliseconds(150));
            var interceptor = new StormGateConnectionInterceptor(stormGate);

            using var context1 = CreateContext(dbPath, interceptor);
            using var context2 = CreateContext(dbPath, interceptor);

            context1.Database.OpenConnection();

            Assert.Throws<TimeoutException>(() => context2.Database.OpenConnection());

            context1.Database.CloseConnection();

            context2.Database.OpenConnection();
            context2.Database.CloseConnection();
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    // Sync counterpart of PermitIsReleased_WhenPhysicalOpenFails — exercises ConnectionFailed.
    [Fact]
    public void PermitIsReleased_WhenPhysicalOpenFails_Sync()
    {
        var dbPath = TempDbPath();
        using var stormGate = CreateGate(dbPath, maxConcurrentOpens: 1, TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        var badConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(Path.GetTempPath(), $"stormgate-efcore-missing-{Guid.NewGuid():N}", "db.sqlite"),
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        var failingOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(badConnectionString)
            .UseStormGate(interceptor)
            .Options;

        using (var failingContext = new TestDbContext(failingOptions))
        {
            Assert.ThrowsAny<Exception>(() => failingContext.Database.OpenConnection());
        }

        try
        {
            using var context = CreateContext(dbPath, interceptor);

            // Would time out if the failed open above had leaked its permit.
            context.Database.OpenConnection();
            context.Database.CloseConnection();
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    // Covers the generic-typed UseStormGate(StormGateConnectionInterceptor) overload, not just
    // the non-generic one exercised by every other test above.
    [Fact]
    public async Task GenericOverload_ConstructsAWorkingInterceptor()
    {
        var dbPath = TempDbPath();
        try
        {
            using var stormGate = CreateGate(dbPath, maxConcurrentOpens: 2, TimeSpan.FromMilliseconds(150));
            var interceptor = new StormGateConnectionInterceptor(stormGate);

            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .UseStormGate(interceptor)
                .Options;

            await using var context = new TestDbContext(options);
            await context.Database.OpenConnectionAsync();
            await context.Database.CloseConnectionAsync();
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    // Regression guard against a subtle over-release or under-release bug: run far more
    // sequential open/close cycles than the gate's capacity and confirm none ever leak.
    [Fact]
    public async Task ManySequentialOpens_NeverLeakPermits()
    {
        var dbPath = TempDbPath();
        try
        {
            using var stormGate = CreateGate(dbPath, maxConcurrentOpens: 1, TimeSpan.FromMilliseconds(150));
            var interceptor = new StormGateConnectionInterceptor(stormGate);

            for (var i = 0; i < 20; i++)
            {
                await using var context = CreateContext(dbPath, interceptor);
                await context.Database.OpenConnectionAsync();
                await context.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task PermitIsReleased_WhenExplicitlyOpenContextIsDisposed()
    {
        var dbPath = TempDbPath();
        try
        {
            using var stormGate = CreateGate(dbPath, maxConcurrentOpens: 1, TimeSpan.FromMilliseconds(150));
            var interceptor = new StormGateConnectionInterceptor(stormGate);

            await using (var context = CreateContext(dbPath, interceptor))
            {
                await context.Database.OpenConnectionAsync();
            }

            await using var nextContext = CreateContext(dbPath, interceptor);
            await nextContext.Database.OpenConnectionAsync();
            await nextContext.Database.CloseConnectionAsync();
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task PooledContexts_ReleasePermitsWhenExplicitlyOpenedConnectionsAreReset()
    {
        var dbPath = TempDbPath();
        try
        {
            using var stormGate = CreateGate(dbPath, maxConcurrentOpens: 1, TimeSpan.FromMilliseconds(150));
            var interceptor = new StormGateConnectionInterceptor(stormGate);
            var services = new ServiceCollection();

            services.AddDbContextPool<TestDbContext>(options =>
                options.UseSqlite("Data Source=" + dbPath).UseStormGate(interceptor));

            await using var provider = services.BuildServiceProvider();

            for (var i = 0; i < 100; i++)
            {
                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
                await context.Database.OpenConnectionAsync();
            }

            await using var finalScope = provider.CreateAsyncScope();
            var finalContext = finalScope.ServiceProvider.GetRequiredService<TestDbContext>();
            await finalContext.Database.OpenConnectionAsync();
            await finalContext.Database.CloseConnectionAsync();
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    // Complements StormGateEfCoreResilienceTests' fakeDb pooled-factory test, which discovered
    // (and documents) that a pooled options delegate embedding an explicit DbConnection instance
    // reuses that SAME connection object across every pooled instance — making genuine
    // per-instance concurrent connections impossible to prove that way. This configuration uses a
    // real connection STRING instead, so EF Core's SQLite provider constructs an actual, distinct
    // SqliteConnection for each pooled instance the factory builds — the one setup where "two
    // concurrently open, genuinely distinct pooled connections" can actually be proven end to end.
    [Fact]
    public async Task PooledContexts_AdmissionControlAppliesAcrossGenuinelyDistinctConcurrentConnections()
    {
        var dbPath = TempDbPath();
        try
        {
            using var stormGate = CreateGate(dbPath, maxConcurrentOpens: 2, TimeSpan.FromMilliseconds(150));
            var interceptor = new StormGateConnectionInterceptor(stormGate);
            var services = new ServiceCollection();

            services.AddDbContextPool<TestDbContext>(options =>
                options.UseSqlite("Data Source=" + dbPath).UseStormGate(interceptor));

            await using var provider = services.BuildServiceProvider();

            // None of the three scopes is disposed before the next is created, so the pool must
            // construct three genuinely distinct pooled instances, each with its own real
            // SqliteConnection.
            await using var scope1 = provider.CreateAsyncScope();
            var context1 = scope1.ServiceProvider.GetRequiredService<TestDbContext>();
            await using var scope2 = provider.CreateAsyncScope();
            var context2 = scope2.ServiceProvider.GetRequiredService<TestDbContext>();
            await using var scope3 = provider.CreateAsyncScope();
            var context3 = scope3.ServiceProvider.GetRequiredService<TestDbContext>();

            await context1.Database.OpenConnectionAsync();
            await context2.Database.OpenConnectionAsync();

            // Both permits are held by two genuinely distinct, concurrently open real connections.
            await Assert.ThrowsAsync<TimeoutException>(() => context3.Database.OpenConnectionAsync());

            await context1.Database.CloseConnectionAsync();

            // The permit context1 released is now available to context3.
            await context3.Database.OpenConnectionAsync();
            await context3.Database.CloseConnectionAsync();

            await context2.Database.CloseConnectionAsync();
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    // Proves the actual architectural fix: raw ADO.NET access through StormGate.OpenAsync() and
    // EF Core access through the interceptor now draw from the SAME admission budget, not two
    // independent ones. Before the fix, this scenario was impossible to even express — the
    // interceptor had no way to consume a StormGate's permits at all.
    [Fact]
    public async Task RawAdoNetOpen_AndEfCoreOpen_ShareTheSameAdmissionBudget()
    {
        var dbPath = TempDbPath();
        try
        {
            using var stormGate = CreateGate(dbPath, maxConcurrentOpens: 1, TimeSpan.FromMilliseconds(150));
            var interceptor = new StormGateConnectionInterceptor(stormGate);

            // Raw ADO.NET (e.g. Dapper-style) consumer takes the only permit.
            var rawConnection = await stormGate.OpenAsync();

            // EF Core must now see the gate as saturated — same budget, not an independent one.
            await using var efContext = CreateContext(dbPath, interceptor);
            await Assert.ThrowsAsync<TimeoutException>(() => efContext.Database.OpenConnectionAsync());

            await rawConnection.CloseAsync();

            // The permit the raw consumer released is now available to EF Core.
            await efContext.Database.OpenConnectionAsync();
            await efContext.Database.CloseConnectionAsync();
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void Constructor_ThrowsForNullStormGate()
    {
        Assert.Throws<ArgumentNullException>(() => new StormGateConnectionInterceptor(null!));
    }

    [Fact]
    public void UseStormGate_ThrowsForNullInterceptor()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();

        Assert.Throws<ArgumentNullException>(() =>
            optionsBuilder.UseStormGate((StormGateConnectionInterceptor)null!));
    }

    [Fact]
    public void UseStormGate_ThrowsForNullOptionsBuilder()
    {
        using var stormGate = CreateGate(TempDbPath(), maxConcurrentOpens: 1, TimeSpan.FromSeconds(1));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        Assert.Throws<ArgumentNullException>(() =>
            StormGateDbContextOptionsBuilderExtensions.UseStormGate(
                (DbContextOptionsBuilder)null!, interceptor));
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"stormgate-efcore-{Guid.NewGuid():N}.db");

    private static StormGate CreateGate(string dbPath, int maxConcurrentOpens, TimeSpan acquireTimeout) =>
        StormGate.Create(SqliteFactory.Instance, $"Data Source={dbPath}", maxConcurrentOpens, acquireTimeout);

    private static TestDbContext CreateContext(string dbPath, StormGateConnectionInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .UseStormGate(interceptor)
            .Options;
        return new TestDbContext(options);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
}
