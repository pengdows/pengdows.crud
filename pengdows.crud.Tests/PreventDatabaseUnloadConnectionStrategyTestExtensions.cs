using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

// Test helpers, moved out of the production assembly (review 2026-09-29).
internal static class PreventDatabaseUnloadConnectionStrategyTestExtensions
{
    // Convenience async helpers expected by tests
    internal static Task<ITrackedConnection> GetConnectionAsync(this PreventDatabaseUnloadConnectionStrategy _,
        DatabaseContext context, ExecutionType executionType, bool isShared)
    {
        var strat = new PreventDatabaseUnloadConnectionStrategy(context);
        var conn = strat.GetConnection(executionType, isShared);
        strat.PostInitialize(conn);
        return Task.FromResult(conn);
    }

    internal static Task CloseConnectionAsync(this PreventDatabaseUnloadConnectionStrategy _, ITrackedConnection? connection,
        DatabaseContext context)
    {
        var strat = new PreventDatabaseUnloadConnectionStrategy(context);
        return strat.ReleaseConnectionAsync(connection).AsTask();
    }
}
