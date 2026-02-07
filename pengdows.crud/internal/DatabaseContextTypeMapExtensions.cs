using System;

namespace pengdows.crud.@internal;

internal static class DatabaseContextTypeMapExtensions
{
    private const string MissingAccessorMessage =
        "IDatabaseContext does not expose an internal TypeMapRegistry.";

    internal static ITypeMapRegistry GetInternalTypeMapRegistry(this IDatabaseContext context)
    {
        if (context is ITypeMapAccessor accessor)
        {
            return accessor.TypeMapRegistry;
        }

        throw new InvalidOperationException(MissingAccessorMessage);
    }

    internal static void RegisterEntity<TEntity>(this IDatabaseContext context)
    {
        context.GetInternalTypeMapRegistry().Register<TEntity>();
    }
}
