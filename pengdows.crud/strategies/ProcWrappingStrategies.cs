namespace pengdows.crud.strategies;

using pengdows.crud.enums;

internal class ExecProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args)
    {
        return string.IsNullOrWhiteSpace(args) ? $"EXEC {procName}" : $"EXEC {procName} {args}";
    }
}

internal class CallProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args)
    {
        return $"CALL {procName}({args})";
    }
}

internal class PostgresProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args)
    {
        return executionType == ExecutionType.Read
            ? $"SELECT * FROM {procName}({args})"
            : $"CALL {procName}({args})";
    }
}

internal class OracleProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args)
    {
        return $"BEGIN\n\t{procName}{(string.IsNullOrEmpty(args) ? string.Empty : $"({args})")};\nEND;";
    }
}

internal class ExecuteProcedureWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args)
    {
        return $"EXECUTE PROCEDURE {procName}({args})";
    }
}

internal class UnsupportedProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args)
    {
        throw new NotSupportedException("Stored procedures are not supported by this database.");
    }
}

internal static class ProcWrappingStrategyFactory
{
    public static IProcWrappingStrategy Create(ProcWrappingStyle style)
    {
        return style switch
        {
            ProcWrappingStyle.Exec => new ExecProcWrappingStrategy(),
            ProcWrappingStyle.Call => new CallProcWrappingStrategy(),
            ProcWrappingStyle.PostgreSQL => new PostgresProcWrappingStrategy(),
            ProcWrappingStyle.Oracle => new OracleProcWrappingStrategy(),
            ProcWrappingStyle.ExecuteProcedure => new ExecuteProcedureWrappingStrategy(),
            _ => new UnsupportedProcWrappingStrategy()
        };
    }
}
