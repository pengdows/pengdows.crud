using System.Collections.Generic;
using pengdows.crud.enums;

namespace pengdows.crud.strategies.proc;

internal static class ProcWrappingStrategyFactory
{
    private static readonly IReadOnlyDictionary<ProcWrappingStyle, IProcWrappingStrategy> _cache =
        new Dictionary<ProcWrappingStyle, IProcWrappingStrategy>
        {
            [ProcWrappingStyle.Exec] = new ExecProcWrappingStrategy(),
            [ProcWrappingStyle.Call] = new CallProcWrappingStrategy(),
            [ProcWrappingStyle.PostgreSQL] = new PostgresProcWrappingStrategy(),
            [ProcWrappingStyle.Oracle] = new OracleProcWrappingStrategy(),
            [ProcWrappingStyle.ExecuteProcedure] = new ExecuteProcedureWrappingStrategy(),
            [ProcWrappingStyle.None] = new UnsupportedProcWrappingStrategy()
        };

    public static IProcWrappingStrategy Create(ProcWrappingStyle style)
    {
        return _cache.TryGetValue(style, out var strategy)
            ? strategy
            : _cache[ProcWrappingStyle.None];
    }
}
