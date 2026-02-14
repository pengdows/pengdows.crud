// =============================================================================
// FILE: ExecProcWrappingStrategy.cs
// PURPOSE: Stored procedure wrapping using T-SQL EXEC syntax (SQL Server).
//
// AI SUMMARY:
// - Generates SQL: "EXEC proc_name args" or "EXEC proc_name" if no args.
// - Used by SQL Server and Sybase.
// - Note: EXEC uses space-separated args, not parenthesized comma-separated.
// - ExecutionType is ignored - EXEC syntax is same for reads and writes.
// - Validates procedure name is not null/empty.
// - Uses wrapObjectName callback for proper identifier quoting if provided.
// =============================================================================

using pengdows.crud.enums;

namespace pengdows.crud.strategies.proc;

/// <summary>
/// Wraps stored procedure calls using T-SQL EXEC syntax.
/// </summary>
/// <remarks>
/// <para>
/// Generates: <c>EXEC procedure_name args</c> or just <c>EXEC procedure_name</c> if no arguments.
/// </para>
/// <para>
/// Note the T-SQL difference: arguments are space-separated after the procedure name,
/// not enclosed in parentheses like CALL syntax.
/// </para>
/// <para>
/// Used by SQL Server, Sybase, and other T-SQL compatible databases.
/// </para>
/// </remarks>
internal class ExecProcWrappingStrategy : IProcWrappingStrategy
{
    /// <inheritdoc/>
    public string Wrap(string procName, ExecutionType executionType, string args,
        Func<string, string>? wrapObjectName = null)
    {
        var wrappedProcName = IProcWrappingStrategy.ValidateAndWrap(procName, wrapObjectName);
        return string.IsNullOrWhiteSpace(args) ? $"EXEC {wrappedProcName}" : $"EXEC {wrappedProcName} {args}";
    }
}