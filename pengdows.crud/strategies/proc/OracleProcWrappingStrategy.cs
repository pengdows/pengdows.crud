using pengdows.crud.enums;

namespace pengdows.crud.strategies.proc;

internal class OracleProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args,
        Func<string, string>? wrapObjectName = null)
    {
        if (string.IsNullOrWhiteSpace(procName))
        {
            throw new ArgumentException("Procedure name cannot be null or empty.", nameof(procName));
        }

        var wrappedProcName = wrapObjectName?.Invoke(procName) ?? procName;
        return $"BEGIN\n\t{wrappedProcName}{(string.IsNullOrEmpty(args) ? string.Empty : $"({args})")};\nEND;";
    }
}