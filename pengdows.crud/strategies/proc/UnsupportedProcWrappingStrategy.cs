namespace pengdows.crud.strategies.proc;

using pengdows.crud.enums;

internal class UnsupportedProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args, Func<string, string>? wrapObjectName = null)
    {
        throw new NotSupportedException("Stored procedures are not supported by this database.");
    }
}