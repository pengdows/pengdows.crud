namespace pengdows.crud.strategies;

using pengdows.crud.enums;

internal interface IProcWrappingStrategy
{
    string Wrap(string procName, ExecutionType executionType, string args);
}
