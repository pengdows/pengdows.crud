using pengdows.crud.infrastructure;

namespace pengdows.crud.@internal;

internal static class InternalConnectionStringAccess
{
    internal static string GetRawConnectionString(IDatabaseContext context)
    {
        return context switch
        {
            DatabaseContext databaseContext => databaseContext.RawConnectionString,
            TransactionContext transactionContext => transactionContext.RawConnectionString,
            _ => context.ConnectionString
        };
    }

    internal static string GetRawReaderConnectionString(IDatabaseContext context)
    {
        return context switch
        {
            DatabaseContext databaseContext => databaseContext.RawReaderConnectionString,
            TransactionContext transactionContext => transactionContext.RawReaderConnectionString,
            _ => context.ConnectionString
        };
    }
}
