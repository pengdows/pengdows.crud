using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

// Item 5 from the independent architecture review: "EF retry strategies may fight StormGate
// fail-fast behavior. Saturation currently throws a normal TimeoutException. EF/provider retry
// logic may classify that as transient and repeatedly retry admission... you probably want a
// test proving exactly what retry strategies do with a saturated gate."
//
// The README already documents that SQL Server's *default* (non-retrying) execution strategy
// re-wraps the saturation TimeoutException in an InvalidOperationException. This file answers the
// stronger, separate question the reviewer actually asked: what happens when the CALLER has
// opted into EnableRetryOnFailure — does that retry policy treat StormGate's fail-fast
// TimeoutException as transient and retry it against the very gate that just said "no room"?
//
// Confirmed by direct reproduction, not assumed: SqlServerRetryingExecutionStrategy's
// ShouldRetryOn treats a raw TimeoutException as retryable, so yes — a caller who enables
// EF Core's built-in retry-on-failure against a StormGate-gated context will retry admission
// attempts against a permanently saturated gate, once per configured retry, before finally
// giving up and surfacing Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException.
// Each retry re-enters StormGateConnectionInterceptor's OpenAsync path and logs its own
// saturation warning — this test counts those to prove the retries are real admission attempts,
// not just EF re-throwing the same exception object without actually retrying.
public sealed class EfRetryStrategyTests
{
    [Fact]
    public async Task SqlServerRetryingExecutionStrategy_TreatsSaturationTimeoutAsTransient_AndRetriesUntilExhausted()
    {
        var saturationWarnings = new List<string>();
        var logger = new CapturingLogger(saturationWarnings);

        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var stormGate = StormGate.Create(
            factory,
            "Data Source=fake",
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(20),
            logger: logger);
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        // Permanently occupies the only permit for the whole test — never closed.
        var holderConnection = factory.CreateConnection()!;
        var holderBuilder = new DbContextOptionsBuilder<RetryProbeContext>();
        holderBuilder.UseSqlServer(holderConnection, contextOwnsConnection: false);
        holderBuilder.UseStormGate(interceptor);
        await using var holder = new RetryProbeContext(holderBuilder.Options);
        await holder.Database.OpenConnectionAsync();

        var retryConnection = factory.CreateConnection()!;
        var retryBuilder = new DbContextOptionsBuilder<RetryProbeContext>();
        retryBuilder.UseSqlServer(
            retryConnection,
            contextOwnsConnection: false,
            sql => sql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromMilliseconds(5),
                errorNumbersToAdd: null));
        retryBuilder.UseStormGate(interceptor);
        await using var db = new RetryProbeContext(retryBuilder.Options);

        // A real query, not Database.OpenConnectionAsync() directly — only operations executed
        // through IExecutionStrategy.ExecuteAsync are retried; opening a connection outside that
        // wrapper is not. This is what actually proves the retry strategy governs the open.
        var thrown = await Assert.ThrowsAsync<RetryLimitExceededException>(() => db.Probes.ToListAsync());

        var saturation = FindStormGateSaturationTimeout(thrown);
        Assert.NotNull(saturation);

        // 1 initial attempt + 3 configured retries = 4 independent admission attempts against
        // the still-saturated gate, each logging its own saturation warning.
        Assert.Equal(4, saturationWarnings.Count(m => m.Contains("saturation", StringComparison.OrdinalIgnoreCase)));
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

    private sealed class CapturingLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (messages)
            {
                messages.Add(formatter(state, exception));
            }
        }
    }

    private sealed class RetryProbeContext(DbContextOptions<RetryProbeContext> options) : DbContext(options)
    {
        public DbSet<Probe> Probes => Set<Probe>();
    }

    private sealed class Probe
    {
        public int Id { get; set; }
    }
}
