using pengdows.crud.dialects;

namespace pengdows.crud;

internal static class InternalDialectProviderExtensions
{
    internal static ISqlDialect GetDialect(this IDatabaseContext context)
    {
        if (context is not ISqlDialectProvider provider || provider.Dialect == null)
        {
            throw new InvalidOperationException("IDatabaseContext must expose a non-null Dialect.");
        }

        return provider.Dialect;
    }
}
