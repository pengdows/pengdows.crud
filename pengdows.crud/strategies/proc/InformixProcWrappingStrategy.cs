// =============================================================================
// FILE: InformixProcWrappingStrategy.cs
// PURPOSE: Stored procedure wrapping for Informix using EXECUTE PROCEDURE syntax.
//
// AI SUMMARY:
// - Generates SQL: "EXECUTE PROCEDURE proc_name(args)" for reads and writes alike.
// - Parentheses are always emitted, even with no args (Informix requires them).
// - Differs from ExecuteProcedureWrappingStrategy (Firebird): no SELECT form for reads, and no
//   omitted parentheses.
// - Validates procedure name is not null/empty.
// - Uses wrapObjectName callback for proper identifier quoting if provided.
// =============================================================================

using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.strategies.proc;

/// <summary>
/// Wraps stored procedure calls using Informix's <c>EXECUTE PROCEDURE</c> statement.
/// </summary>
/// <remarks>
/// <para>
/// Generates: <c>EXECUTE PROCEDURE procedure_name(args)</c> for every execution type.
/// </para>
/// <para>
/// Informix documents EXECUTE PROCEDURE / EXECUTE FUNCTION as the stand-alone statements for
/// running a routine; CALL is documented as valid only inside an SPL routine. Verified live against
/// Informix 15.0.1.0.3: EXECUTE PROCEDURE runs a procedure with or without a RETURNING clause and a
/// CREATE FUNCTION routine, returning any value as a result row. EXECUTE FUNCTION cannot run a
/// procedure that returns nothing, "SELECT * FROM name(args)" is a syntax error, and the
/// parentheses are required even with no arguments.
/// </para>
/// </remarks>
internal class InformixProcWrappingStrategy : IProcWrappingStrategy
{
    /// <inheritdoc/>
    public string Wrap(string procName, ExecutionType executionType, string args,
        Func<string, string>? wrapObjectName = null)
    {
        var wrappedProcName = IProcWrappingStrategy.ValidateAndWrap(procName, wrapObjectName);
        return $"EXECUTE PROCEDURE {wrappedProcName}({args})";
    }
}
