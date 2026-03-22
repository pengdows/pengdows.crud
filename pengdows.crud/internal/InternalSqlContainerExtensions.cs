using System.Data.Common;
using pengdows.crud.wrappers;

namespace pengdows.crud;

internal static class InternalSqlContainerExtensions
{
    internal static DbCommand CreateCommand(this ISqlContainer container, ITrackedConnection connection)
    {
        if (container is not SqlContainer sqlContainer)
        {
            throw new InvalidOperationException("ISqlContainer must be a SqlContainer instance.");
        }

        return sqlContainer.CreateCommand(connection);
    }
}
