// =============================================================================
// FILE: CallProcWrappingStrategy.cs
// PURPOSE: Stored procedure wrapping using CALL syntax (MySQL, MariaDB, DB2).
//
// AI SUMMARY:
// - Generates SQL: "CALL proc_name(args)"
// - Used by MySQL, MariaDB, DB2, and other SQL standard-compliant databases.
// - ExecutionType is ignored - CALL syntax is same for reads and writes.
// - Validates procedure name is not null/empty.
// - Uses wrapObjectName callback for proper identifier quoting if provided.
// =============================================================================

using pengdows.crud.enums;

namespace pengdows.crud.strategies.proc;

/// <summary>
/// Wraps stored procedure calls using SQL standard CALL syntax.
/// </summary>
/// <remarks>
/// <para>
/// Generates: <c>CALL procedure_name(args)</c>
/// </para>
/// <para>
/// Used by databases that follow SQL standard stored procedure invocation:
/// MySQL, MariaDB, DB2, and others.
/// </para>
/// </remarks>
internal class CallProcWrappingStrategy : IProcWrappingStrategy
{
    /// <inheritdoc/>
    public string Wrap(string procName, ExecutionType executionType, string args,
        Func<string, string>? wrapObjectName = null)
    {
        if (string.IsNullOrWhiteSpace(procName))
        {
            throw new ArgumentException(IProcWrappingStrategy.ProcNameNullOrEmptyMessage, nameof(procName));
        }

        var wrappedProcName = wrapObjectName?.Invoke(procName) ?? procName;
        return $"CALL {wrappedProcName}({args})";
    }
}