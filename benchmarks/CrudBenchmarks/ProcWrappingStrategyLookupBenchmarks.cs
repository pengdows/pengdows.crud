using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;
using pengdows.crud.enums;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 12)]
public class ProcWrappingStrategyLookupBenchmarks
{
    private static readonly Dictionary<ProcWrappingStyle, object> MutableCache =
        new()
        {
            [ProcWrappingStyle.Exec] = new object(),
            [ProcWrappingStyle.Call] = new object(),
            [ProcWrappingStyle.PostgreSQL] = new object(),
            [ProcWrappingStyle.Oracle] = new object(),
            [ProcWrappingStyle.ExecuteProcedure] = new object(),
            [ProcWrappingStyle.None] = new object()
        };

    private static readonly FrozenDictionary<ProcWrappingStyle, object> FrozenCache =
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