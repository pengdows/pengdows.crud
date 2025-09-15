using pengdows.crud.enums;

namespace pengdows.crud.strategies.connection;

internal static class ConnectionStrategyFactory
{
    public static  IConnectionStrategy Create(DatabaseContext context, DbMode mode)
    {
        // If an embedded in-memory engine coerces the mode to SingleConnection, but the user
        // explicitly requested KeepAlive, prefer KeepAlive semantics while reporting SingleConnection
        if (mode == DbMode.SingleConnection && context.OriginalUserMode == DbMode.KeepAlive)
        {
            return new KeepAliveConnectionStrategy(context);
        }

        return mode switch
        {
            DbMode.Standard => new StandardConnectionStrategy(context),
            DbMode.KeepAlive => new KeepAliveConnectionStrategy(context),
            DbMode.SingleConnection => new SingleConnectionStrategy(context),
            DbMode.SingleWriter => new SingleWriterConnectionStrategy(context),
            _ => new StandardConnectionStrategy(context)
        };
    }
}
