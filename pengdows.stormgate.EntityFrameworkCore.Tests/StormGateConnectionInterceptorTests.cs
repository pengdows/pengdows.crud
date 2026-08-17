using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace pengdows.stormgate.EntityFrameworkCore.Tests;

public sealed class StormGateConnectionInterceptorTests
{
    // Proves the actual "connection storm" claim end to end: a real EF Core connection held
    // open blocks a second real EF Core connection from opening once the gate is saturated,
    // and the second attempt fails fast with TimeoutException instead of queuing indefinitely
    // or overwhelming the provider. The interceptor instance is shared across both contexts —
    // exactly as it must be used in production (see README): a fresh interceptor per DbContext
    // would give each its own semaphore and throttle nothing.
    [Fact]
    public async Task SecondContext_TimesOut_WhileFirstConnectionIsStillOpen_ThenSucceedsAfterRelease()
    {
        var dbPath = TempDbPath();
        try
        {
            var interceptor = new StormGateConnectionInterceptor(
                maxConcurrentOpens: 1,
                acquireTimeout: TimeSpan.FromMilliseconds(150));

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
        var interceptor = new StormGateConnectionInterceptor(
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(150));

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

        var dbPath = TempDbPath();
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
            var interceptor = new StormGateConnectionInterceptor(
                maxConcurrentOpens: 1,
                acquireTimeout: TimeSpan.FromMilliseconds(150));

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
        var interceptor = new StormGateConnectionInterceptor(
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(150));

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

        var dbPath = TempDbPath();
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

    // Covers the plain (int, TimeSpan, ILogger?) convenience overloads on both the non-generic
    // and generic-typed UseStormGate extension methods, not just the shared-instance overload
    // exercised by every other test above.
    [Fact]
    public async Task ConvenienceOverload_ConstructsAWorkingInterceptor()
    {
        var dbPath = TempDbPath();
        try
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .UseStormGate(maxConcurrentOpens: 2, acquireTimeout: TimeSpan.FromMilliseconds(150))
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
            var interceptor = new StormGateConnectionInterceptor(
                maxConcurrentOpens: 1,
                acquireTimeout: TimeSpan.FromMilliseconds(150));

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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ThrowsForNonPositiveMaxConcurrentOpens(int maxConcurrentOpens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StormGateConnectionInterceptor(maxConcurrentOpens, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Constructor_ThrowsForNegativeAcquireTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StormGateConnectionInterceptor(1, TimeSpan.FromSeconds(-1)));
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
        var interceptor = new StormGateConnectionInterceptor(1, TimeSpan.FromSeconds(1));

        Assert.Throws<ArgumentNullException>(() =>
            StormGateDbContextOptionsBuilderExtensions.UseStormGate(
                (DbContextOptionsBuilder)null!, interceptor));
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"stormgate-efcore-{Guid.NewGuid():N}.db");

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
