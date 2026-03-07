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
}
