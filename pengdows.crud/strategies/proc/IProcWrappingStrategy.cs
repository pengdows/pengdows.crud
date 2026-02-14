// =============================================================================
// FILE: IProcWrappingStrategy.cs
// PURPOSE: Interface for stored procedure call syntax wrapping strategies.
//
// AI SUMMARY:
// - Defines contract for database-specific stored procedure invocation syntax.
// - Single method: Wrap(procName, executionType, args, wrapObjectName).
// - Different databases use different syntax: EXEC, CALL, BEGIN/END, SELECT FROM.
// - executionType determines read vs write semantics for some databases.
// - wrapObjectName callback allows proper identifier quoting.
// - Implementations: ExecProcWrappingStrategy (SQL Server), CallProcWrappingStrategy,
//   PostgresProcWrappingStrategy, OracleProcWrappingStrategy, ExecuteProcedureWrappingStrategy,
//   UnsupportedProcWrappingStrategy.
// =============================================================================

using pengdows.crud.enums;

namespace pengdows.crud.strategies.proc;

/// <summary>
/// Defines the contract for database-specific stored procedure call syntax wrapping.
/// </summary>
/// <remarks>
/// <para>
/// Different databases use different syntax to invoke stored procedures:
/// SQL Server uses EXEC, MySQL uses CALL, Oracle uses PL/SQL blocks, etc.
/// </para>
/// <para>
/// Implementations are selected by <see cref="ProcWrappingStrategyFactory"/>
/// based on the dialect's <see cref="ProcWrappingStyle"/>.
/// </para>
/// </remarks>
internal interface IProcWrappingStrategy
{
    /// <summary>Shared validation message for null-or-empty procedure names.</summary>
    const string ProcNameNullOrEmptyMessage = "Procedure name cannot be null or empty.";

    /// <summary>
    /// Wraps a stored procedure call in the appropriate database-specific syntax.
    /// </summary>
    /// <param name="procName">The name of the stored procedure to invoke.</param>
    /// <param name="executionType">Read or Write - affects syntax for some databases (PostgreSQL, Firebird).</param>
    /// <param name="args">Comma-separated parameter placeholders (e.g., "@p0, @p1, @p2").</param>
    /// <param name="wrapObjectName">Optional callback to quote identifiers properly for the target database.</param>
    /// <returns>Complete SQL statement to execute the stored procedure.</returns>
    string Wrap(string procName, ExecutionType executionType, string args, Func<string, string>? wrapObjectName = null);

    /// <summary>
    /// Validates that a procedure name is not null/empty and applies optional identifier wrapping.
    /// Shared by all strategy implementations to eliminate duplicated validation logic.
    /// </summary>
    /// <param name="procName">The procedure name to validate.</param>
    /// <param name="wrapObjectName">Optional callback to quote identifiers.</param>
    /// <returns>The validated and optionally wrapped procedure name.</returns>
    static string ValidateAndWrap(string procName, Func<string, string>? wrapObjectName)
    {
        if (string.IsNullOrWhiteSpace(procName))
        {
            throw new ArgumentException(ProcNameNullOrEmptyMessage, nameof(procName));
        }

        return wrapObjectName?.Invoke(procName) ?? procName;
    }
}