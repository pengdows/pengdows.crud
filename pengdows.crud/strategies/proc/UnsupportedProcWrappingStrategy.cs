// =============================================================================
// FILE: UnsupportedProcWrappingStrategy.cs
// PURPOSE: Fallback strategy for databases without stored procedure support.
//
// AI SUMMARY:
// - Used for databases that don't support stored procedures (SQLite, DuckDB).
// - Always throws NotSupportedException when Wrap() is called.
// - Returned by factory for ProcWrappingStyle.None or unknown styles.
// - Provides clear error message explaining stored procs aren't available.
// =============================================================================

using pengdows.crud.enums;

namespace pengdows.crud.strategies.proc;

/// <summary>
/// Fallback strategy that throws for databases without stored procedure support.
/// </summary>
/// <remarks>
/// Used for SQLite, DuckDB, and other databases that don't support traditional
/// stored procedures. Attempting to invoke a stored procedure will throw
/// <see cref="NotSupportedException"/>.
/// </remarks>
internal class UnsupportedProcWrappingStrategy : IProcWrappingStrategy
{
    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always thrown - stored procedures are not supported.</exception>
    public string Wrap(string procName, ExecutionType executionType, string args,
        Func<string, string>? wrapObjectName = null)
    {
        throw new NotSupportedException("Stored procedures are not supported by this database.");
    }
}