using pengdows.crud.enums;

namespace pengdows.crud.strategies.proc;

internal class ExecuteProcedureWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args, Func<string, string>? wrapObjectName = null)
    {
        if (string.IsNullOrWhiteSpace(procName))
        {
            throw new ArgumentException("Procedure name cannot be null or empty.", nameof(procName));
        }
        
        var wrappedProcName = wrapObjectName?.Invoke(procName) ?? procName;
        return executionType == ExecutionType.Read
            ? $"SELECT * FROM {wrappedProcName}({args})"
            : $"EXECUTE PROCEDURE {wrappedProcName}({args})";
    }
}