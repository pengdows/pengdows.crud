namespace pengdows.crud.strategies.proc;

using pengdows.crud.enums;

internal class ExecProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args, Func<string, string>? wrapObjectName = null)
    {
        if (string.IsNullOrWhiteSpace(procName))
        {
            throw new ArgumentException("Procedure name cannot be null or empty.", nameof(procName));
        }
        
        var wrappedProcName = wrapObjectName?.Invoke(procName) ?? procName;
        return string.IsNullOrWhiteSpace(args) ? $"EXEC {wrappedProcName}" : $"EXEC {wrappedProcName} {args}";
    }
}