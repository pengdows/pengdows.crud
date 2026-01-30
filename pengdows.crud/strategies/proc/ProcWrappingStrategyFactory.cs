// =============================================================================
// FILE: ProcWrappingStrategyFactory.cs
// PURPOSE: Factory for creating stored procedure wrapping strategies by style.
//
// AI SUMMARY:
// - Static factory returning IProcWrappingStrategy based on ProcWrappingStyle enum.
// - Cached instances for each style (no allocation on Create calls).
// - Style mapping:
//   * Exec -> ExecProcWrappingStrategy (SQL Server: EXEC proc args)
//   * Call -> CallProcWrappingStrategy (MySQL: CALL proc(args))
//   * PostgreSQL -> PostgresProcWrappingStrategy (SELECT FROM for reads, CALL for writes)
//   * Oracle -> OracleProcWrappingStrategy (BEGIN proc(args); END;)
//   * ExecuteProcedure -> ExecuteProcedureWrappingStrategy (Firebird)
//   * None -> UnsupportedProcWrappingStrategy (throws NotSupportedException)
// - Falls back to UnsupportedProcWrappingStrategy for unknown styles.
// =============================================================================

using pengdows.crud.enums;

namespace pengdows.crud.strategies.proc;

/// <summary>
/// Factory for creating stored procedure wrapping strategies based on database style.
/// </summary>
/// <remarks>
/// Maintains a cached dictionary of strategy instances for efficient reuse.
/// Each dialect specifies its <see cref="ProcWrappingStyle"/> which determines
/// how stored procedure calls are formatted.
/// </remarks>
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

    /// <summary>
    /// Creates or retrieves the appropriate proc wrapping strategy for the given style.
    /// </summary>
    /// <param name="style">The stored procedure wrapping style for the target database.</param>
    /// <returns>An <see cref="IProcWrappingStrategy"/> instance for the specified style.</returns>
    public static IProcWrappingStrategy Create(ProcWrappingStyle style)
    {
        return _cache.TryGetValue(style, out var strategy)
            ? strategy
            : _cache[ProcWrappingStyle.None];
    }
}