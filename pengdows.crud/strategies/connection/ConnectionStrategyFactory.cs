namespace pengdows.crud.strategies.connection;


using pengdows.crud.enums;

internal static class ConnectionStrategyFactory
{
    public static  IConnectionStrategy Create(DatabaseContext context, DbMode mode)
    {
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
