namespace pengdows.crud.strategies.proc;

using pengdows.crud.enums;

internal class ExecuteProcedureWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args)
    {
        if (string.IsNullOrWhiteSpace(procName))
        {
            throw new ArgumentException("Procedure name cannot be null or empty.", nameof(procName));
        }
        return executionType == ExecutionType.Read
            ? $"SELECT * FROM {procName}({args})"
            : $"EXECUTE PROCEDURE {procName}({args})";
    }
}