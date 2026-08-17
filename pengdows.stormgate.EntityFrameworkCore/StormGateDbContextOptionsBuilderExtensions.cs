using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace pengdows.stormgate.EntityFrameworkCore;

public static class StormGateDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Gates connection opens for this <see cref="DbContextOptionsBuilder"/> through an
    /// existing, shared <see cref="StormGateConnectionInterceptor"/>. Pass the SAME
    /// interceptor instance to every <c>DbContextOptionsBuilder</c> you want gated together —
    /// e.g. a singleton registered in DI — so concurrency is capped across every
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> instance that shares it, not just
    /// within one instance.
    /// </summary>
    public static DbContextOptionsBuilder UseStormGate(
        this DbContextOptionsBuilder optionsBuilder,
        StormGateConnectionInterceptor interceptor)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(interceptor);

        return optionsBuilder.AddInterceptors(interceptor);
    }

    /// <summary>
    /// Convenience overload that constructs a new <see cref="StormGateConnectionInterceptor"/>
    /// inline. Only safe when this options-configuration delegate itself runs once and its
    /// result is reused across every gated <c>DbContext</c> instance — e.g.
    /// <c>AddDbContextPool</c>/<c>AddDbContextFactory</c>'s single shared options template, or
    /// a process with exactly one long-lived <c>DbContext</c>. Plain, non-pooled
    /// <c>AddDbContext</c> re-runs its options delegate for every request-scoped instance,
    /// which would give each instance its own semaphore and throttle nothing across requests —
    /// use the <see cref="UseStormGate(DbContextOptionsBuilder, StormGateConnectionInterceptor)"/>
    /// overload with a shared singleton instance for that case instead.
    /// </summary>
    public static DbContextOptionsBuilder UseStormGate(
        this DbContextOptionsBuilder optionsBuilder,
        int maxConcurrentOpens,
        TimeSpan acquireTimeout,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        return optionsBuilder.UseStormGate(
            new StormGateConnectionInterceptor(maxConcurrentOpens, acquireTimeout, logger));
    }

    /// <summary>
    /// Generic-typed overload preserving <see cref="DbContextOptionsBuilder{TContext}"/>
    /// fluency — see <see cref="UseStormGate(DbContextOptionsBuilder, StormGateConnectionInterceptor)"/>.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseStormGate<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        StormGateConnectionInterceptor interceptor)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).UseStormGate(interceptor);
        return optionsBuilder;
    }

    /// <summary>
    /// Generic-typed overload preserving <see cref="DbContextOptionsBuilder{TContext}"/>
    /// fluency — see <see cref="UseStormGate(DbContextOptionsBuilder, int, TimeSpan, ILogger?)"/>.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseStormGate<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        int maxConcurrentOpens,
        TimeSpan acquireTimeout,
        ILogger? logger = null)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).UseStormGate(maxConcurrentOpens, acquireTimeout, logger);
        return optionsBuilder;
    }
}
