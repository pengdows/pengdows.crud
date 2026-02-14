// =============================================================================
// FILE: ExecuteProcedureWrappingStrategy.cs
// PURPOSE: Stored procedure wrapping for Firebird using EXECUTE PROCEDURE syntax.
//
// AI SUMMARY:
// - Firebird-specific stored procedure invocation strategy.
// - Read operations: "SELECT * FROM proc_name(args)" - treats proc as table function.
// - Write operations: "EXECUTE PROCEDURE proc_name(args)" - standard Firebird syntax.
// - ExecutionType IS significant - determines which syntax is used.
// - Validates procedure name is not null/empty.
// - Uses wrapObjectName callback for proper identifier quoting if provided.
// =============================================================================

using pengdows.crud.enums;

namespace pengdows.crud.strategies.proc;

/// <summary>
/// Wraps stored procedure calls using Firebird's EXECUTE PROCEDURE syntax.
/// </summary>
/// <remarks>
/// <para>
/// Firebird distinguishes between procedures that return data (selectable) and
/// procedures that perform actions (executable).
/// </para>
/// <para>
/// For read operations: <c>SELECT * FROM procedure_name(args)</c>
/// </para>
/// <para>
/// For write operations: <c>EXECUTE PROCEDURE procedure_name(args)</c>
/// </para>
/// </remarks>
internal class ExecuteProcedureWrappingStrategy : IProcWrappingStrategy
{
    /// <inheritdoc/>
    public string Wrap(string procName, ExecutionType executionType, string args,
        Func<string, string>? wrapObjectName = null)
    {
        var wrappedProcName = IProcWrappingStrategy.ValidateAndWrap(procName, wrapObjectName);
        return executionType == ExecutionType.Read
            ? $"SELECT * FROM {wrappedProcName}({args})"
            : $"EXECUTE PROCEDURE {wrappedProcName}({args})";
    }
}