// =============================================================================
// FILE: PostgresProcWrappingStrategy.cs
// PURPOSE: Stored procedure wrapping for PostgreSQL with function/procedure distinction.
//
// AI SUMMARY:
// - PostgreSQL-specific stored procedure/function invocation strategy.
// - Read operations: "SELECT * FROM func_name(args)" - for functions returning results.
// - Write operations: "CALL proc_name(args)" - for procedures (PostgreSQL 11+).
// - ExecutionType IS significant - determines which syntax is used.
// - Pre-PostgreSQL 11: Only functions existed, use Read for everything.
// - Validates procedure name is not null/empty.
// - Uses wrapObjectName callback for proper identifier quoting if provided.
// - Also used by CockroachDB which is PostgreSQL-compatible.
// =============================================================================

using pengdows.crud.enums;

namespace pengdows.crud.strategies.proc;

/// <summary>
/// Wraps stored procedure calls using PostgreSQL syntax.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL distinguishes between functions (return data) and procedures (perform actions).
/// </para>
/// <para>
/// For read operations (functions): <c>SELECT * FROM function_name(args)</c>
/// </para>
/// <para>
/// For write operations (procedures): <c>CALL procedure_name(args)</c>
/// </para>
/// <para>
/// Note: CALL syntax requires PostgreSQL 11+. Earlier versions only supported functions.
/// </para>
/// </remarks>
internal class PostgresProcWrappingStrategy : IProcWrappingStrategy
{
    /// <inheritdoc/>
    public string Wrap(string procName, ExecutionType executionType, string args,
        Func<string, string>? wrapObjectName = null)
    {
        var wrappedProcName = IProcWrappingStrategy.ValidateAndWrap(procName, wrapObjectName);
        return executionType == ExecutionType.Read
            ? $"SELECT * FROM {wrappedProcName}({args})"
            : $"CALL {wrappedProcName}({args})";
    }
}