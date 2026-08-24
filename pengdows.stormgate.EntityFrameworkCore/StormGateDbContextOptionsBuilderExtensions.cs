using Microsoft.EntityFrameworkCore;

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
}
