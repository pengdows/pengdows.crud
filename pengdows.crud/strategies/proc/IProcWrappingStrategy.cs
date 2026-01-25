using pengdows.crud.enums;

namespace pengdows.crud.strategies.proc;

internal interface IProcWrappingStrategy
{
    string Wrap(string procName, ExecutionType executionType, string args, Func<string, string>? wrapObjectName = null);
}