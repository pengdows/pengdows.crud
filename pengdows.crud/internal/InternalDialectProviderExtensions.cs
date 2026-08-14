using pengdows.crud.dialects;

namespace pengdows.crud;

internal static class InternalDialectProviderExtensions
{
    internal static ISqlDialect GetDialect(this IDatabaseContext context)
    {
        if (context.Dialect == null)
        {
            throw new InvalidOperationException("IDatabaseContext must expose a non-null Dialect.");
        }

        return context.Dialect;
    }

    /// <summary>
    /// Cache key for the gateway caches that hold SQL text/metadata (not baked
    /// <see cref="System.Data.Common.DbParameter"/> construction) — see
    /// <see cref="IInternalSqlDialect.CacheFingerprint"/>.
    /// </summary>
    internal static string GetCacheFingerprint(this ISqlDialect dialect)
    {
        if (dialect is not IInternalSqlDialect internalDialect)
        {
            throw new InvalidOperationException("ISqlDialect must support internal caching operations.");
        }

        return internalDialect.CacheFingerprint;
    }
}
