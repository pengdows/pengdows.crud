namespace pengdows.crud.strategies.proc;

using pengdows.crud.enums;

internal class OracleProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args)
    {
        if (string.IsNullOrWhiteSpace(procName))
        {
            throw new ArgumentException("Procedure name cannot be null or empty.", nameof(procName));
        }
        return $"BEGIN\n\t{procName}{(string.IsNullOrEmpty(args) ? string.Empty : $"({args})")};\nEND;";
    }
}