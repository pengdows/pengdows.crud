namespace pengdows.crud.strategies;

using pengdows.crud.enums;

internal class ExecProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args)
    {
        if (string.IsNullOrWhiteSpace(procName))
        {
            throw new ArgumentException("Procedure name cannot be null or empty.", nameof(procName));
        }
        return string.IsNullOrWhiteSpace(args) ? $"EXEC {procName}" : $"EXEC {procName} {args}";
    }
}

internal class CallProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args)
    {
        if (string.IsNullOrWhiteSpace(procName))
        {
            throw new ArgumentException("Procedure name cannot be null or empty.", nameof(procName));
        }
        return $"CALL {procName}({args})";
    }
}

internal class PostgresProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args)
    {
        if (string.IsNullOrWhiteSpace(procName))
        {
            throw new ArgumentException("Procedure name cannot be null or empty.", nameof(procName));
        }
        return executionType == ExecutionType.Read
            ? $"SELECT * FROM {procName}({args})"
            : $"CALL {procName}({args})";
    }
}

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

internal class UnsupportedProcWrappingStrategy : IProcWrappingStrategy
{
    public string Wrap(string procName, ExecutionType executionType, string args)
    {
        throw new NotSupportedException("Stored procedures are not supported by this database.");
    }
}

internal static class ProcWrappingStrategyFactory
{
    private static readonly IReadOnlyDictionary<ProcWrappingStyle, IProcWrappingStrategy> _cache =
        new Dictionary<ProcWrappingStyle, IProcWrappingStrategy>
        {
            [ProcWrappingStyle.Exec] = new ExecProcWrappingStrategy(),
            [ProcWrappingStyle.Call] = new CallProcWrappingStrategy(),
            [ProcWrappingStyle.PostgreSQL] = new PostgresProcWrappingStrategy(),
            [ProcWrappingStyle.Oracle] = new OracleProcWrappingStrategy(),
            [ProcWrappingStyle.ExecuteProcedure] = new ExecuteProcedureWrappingStrategy(),
            [ProcWrappingStyle.None] = new UnsupportedProcWrappingStrategy()
        };

    public static IProcWrappingStrategy Create(ProcWrappingStyle style)
    {
        return _cache.TryGetValue(style, out var strategy)
            ? strategy
            : _cache[ProcWrappingStyle.None];
    }
}
