using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace pengdows.crud.analyzers;

/// <summary>
/// Shared recognition logic for the gateway-context analyzer pair: PGC025
/// (<see cref="GatewayMethodContextParameterAnalyzer"/>, definition-side — a gateway method that
/// executes database work must resolve and forward its own context parameter) and PGC027
/// (<see cref="GatewayCallSiteContextAnalyzer"/>, call-site-side — under opt-in multitenancy
/// enforcement, a call to a gateway execution method must supply a non-null context argument).
/// Kept in one place so "what counts as a gateway" and "what counts as an execution method" can
/// never drift between the two rules.
/// </summary>
internal static class GatewayAnalysisHelpers
{
    /// <summary>
    /// Tier-1/2/3 gateway methods that build or execute database work. Anything not in this list
    /// (a custom helper method, a plain property, etc.) is out of scope for both PGC025 and PGC027.
    /// </summary>
    internal static readonly ImmutableHashSet<string> ExecutionMethodNames =
    [
        "BeginTransaction",
        "BeginTransactionAsync",
        "RetrieveOneAsync",
        "RetrieveAsync",
        "RetrieveStreamAsync",
        "CreateAsync",
        "UpdateAsync",
        "DeleteAsync",
        "UpsertAsync",
        "BatchCreateAsync",
        "BatchUpdateAsync",
        "BatchUpsertAsync",
        "BatchDeleteAsync",
        "LoadSingleAsync",
        "LoadListAsync",
        "LoadStreamAsync",
        "ExecuteNonQueryAsync",
        "ExecuteScalarRequiredAsync",
        "ExecuteScalarOrNullAsync",
        "TryExecuteScalarAsync",
        "ExecuteReaderAsync",
        "CountAllAsync",
        "CountWhereAsync",
        "CountWhereNullAsync",
        "CountWhereEqualsAsync",
        "BuildCreate",
        "BuildCreateWithReturning",
        "BuildRetrieve",
        "BuildBaseRetrieve",
        "BuildUpdate",
        "BuildUpdateAsync",
        "BuildDelete",
        "BuildBatchCreate",
        "BuildBatchUpdate",
        "BuildBatchDelete",
        "BuildBatchUpsert",
        "BuildUpsert"
    ];

    /// <summary>
    /// Walks a type's inheritance chain (and directly-implemented interfaces at each level) to
    /// decide whether it's a pengdows.crud gateway — <c>TableGateway</c>/<c>PrimaryKeyTableGateway</c>/
    /// <c>BaseTableGateway</c> or a type implementing <c>ITableGateway</c>/<c>IPrimaryKeyTableGateway</c>.
    /// </summary>
    internal static bool IsGatewayType(INamedTypeSymbol? type)
    {
        while (type != null)
        {
            if (type.Name is "TableGateway" or "PrimaryKeyTableGateway" or "BaseTableGateway")
            {
                return true;
            }

            foreach (var iface in type.AllInterfaces)
            {
                if (iface.Name is "ITableGateway" or "IPrimaryKeyTableGateway")
                {
                    return true;
                }
            }

            type = type.BaseType;
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="type"/> is (or implements) <c>IDatabaseContext</c> or
    /// <c>ITransactionContext</c> — the two execution-context parameter types both rules care about.
    /// </summary>
    internal static bool IsContextType(ITypeSymbol type)
    {
        if (type.Name is "IDatabaseContext" or "ITransactionContext")
        {
            return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.Name is "IDatabaseContext" or "ITransactionContext")
            {
                return true;
            }
        }

        return false;
    }
}
