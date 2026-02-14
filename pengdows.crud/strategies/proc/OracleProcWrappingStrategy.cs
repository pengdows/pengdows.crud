// =============================================================================
// FILE: OracleProcWrappingStrategy.cs
// PURPOSE: Stored procedure wrapping using Oracle PL/SQL anonymous block syntax.
//
// AI SUMMARY:
// - Oracle-specific stored procedure invocation using PL/SQL blocks.
// - Generates: "BEGIN proc_name(args); END;" wrapped in anonymous block.
// - Arguments are optional - omits parens if args is empty.
// - ExecutionType is ignored - PL/SQL block syntax is same for reads and writes.
// - Required because Oracle stored procedures cannot be called directly in SQL.
// - Validates procedure name is not null/empty.
// - Uses wrapObjectName callback for proper identifier quoting if provided.
// =============================================================================

using pengdows.crud.enums;

namespace pengdows.crud.strategies.proc;

/// <summary>
/// Wraps stored procedure calls using Oracle PL/SQL anonymous block syntax.
/// </summary>
/// <remarks>
/// <para>
/// Oracle requires stored procedures to be called within PL/SQL blocks:
/// </para>
/// <code>
/// BEGIN
///     procedure_name(args);
/// END;
/// </code>
/// <para>
/// Arguments are optional - parentheses are omitted if no arguments are provided.
/// </para>
/// </remarks>
internal class OracleProcWrappingStrategy : IProcWrappingStrategy
{
    /// <inheritdoc/>
    public string Wrap(string procName, ExecutionType executionType, string args,
        Func<string, string>? wrapObjectName = null)
    {
        var wrappedProcName = IProcWrappingStrategy.ValidateAndWrap(procName, wrapObjectName);
        return $"BEGIN\n\t{wrappedProcName}{(string.IsNullOrEmpty(args) ? string.Empty : $"({args})")};\nEND;";
    }
}