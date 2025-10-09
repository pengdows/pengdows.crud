using System.Collections.Frozen;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using pengdows.crud.enums;
using pengdows.crud.strategies.proc;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 12)]
public class ProcWrappingStrategyLookupBenchmarks
{
    private static readonly Dictionary<ProcWrappingStyle, IProcWrappingStrategy> MutableCache =
        new()
        {
            [ProcWrappingStyle.Exec] = new ExecProcWrappingStrategy(),
            [ProcWrappingStyle.Call] = new CallProcWrappingStrategy(),
            [ProcWrappingStyle.PostgreSQL] = new PostgresProcWrappingStrategy(),
            [ProcWrappingStyle.Oracle] = new OracleProcWrappingStrategy(),
            [ProcWrappingStyle.ExecuteProcedure] = new ExecuteProcedureWrappingStrategy(),
            [ProcWrappingStyle.None] = new UnsupportedProcWrappingStrategy()
        };

    private static readonly FrozenDictionary<ProcWrappingStyle, IProcWrappingStrategy> FrozenCache =
        MutableCache.ToFrozenDictionary();

    private static readonly ProcWrappingStyle[] LookupSequence =
    {
        ProcWrappingStyle.Exec,
        ProcWrappingStyle.Call,
        ProcWrappingStyle.PostgreSQL,
        ProcWrappingStyle.Oracle,
        ProcWrappingStyle.ExecuteProcedure,
        ProcWrappingStyle.None,
        (ProcWrappingStyle)999
    };

    [Benchmark(Baseline = true)]
    public int MutableDictionaryLookup()
    {
        var hash = 0;
        foreach (var style in LookupSequence)
        {
            if (MutableCache.TryGetValue(style, out var strategy))
            {
                hash ^= strategy.GetHashCode();
            }
        }

        return hash;
    }

    [Benchmark]
    public int FrozenDictionaryLookup()
    {
        var hash = 0;
        foreach (var style in LookupSequence)
        {
            if (FrozenCache.TryGetValue(style, out var strategy))
            {
                hash ^= strategy.GetHashCode();
            }
        }

        return hash;
    }
}
