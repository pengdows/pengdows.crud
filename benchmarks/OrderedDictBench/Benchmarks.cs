using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using PengdowsOrdered = pengdows.crud.collections.OrderedDictionary<string, object?>;

#if NET9_0_OR_GREATER
using BclOrdered = System.Collections.Generic.OrderedDictionary<string, object?>;
#endif

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class OrderedDictionaryBench
{
    // Tunable sizes for "DB parameters" style usage.
    [Params(4, 8, 16, 64, 256)]
    public int N;

    private string[] _keys = default!;
    private object?[] _values = default!;
    private string[] _missKeys = default!;

    // Pre-built instances for lookup/enumeration/remove benchmarks.
    private PengdowsOrdered _peng = default!;

#if NET9_0_OR_GREATER
    private BclOrdered _bcl = default!;
#endif

    [GlobalSetup]
    public void Setup()
    {
        _keys = Enumerable.Range(0, N).Select(i => "p" + i.ToString()).ToArray();
        _values = Enumerable.Range(0, N).Select(i => (object?)i).ToArray();
        _missKeys = Enumerable.Range(N, N).Select(i => "p" + i.ToString()).ToArray();

        _peng = new PengdowsOrdered(capacity: N);
        for (int i = 0; i < N; i++)
            _peng.Add(_keys[i], _values[i]);

#if NET9_0_OR_GREATER
        _bcl = new BclOrdered(capacity: N);
        for (int i = 0; i < N; i++)
            _bcl.Add(_keys[i], _values[i]);
#endif
    }

    // ----------------------------
    // Build
    // ----------------------------

    [BenchmarkCategory("Build")]
    [Benchmark(Baseline = true)]
    public PengdowsOrdered Pengdows_Add()
    {
        var d = new PengdowsOrdered(capacity: N);
        for (int i = 0; i < N; i++)
            d.Add(_keys[i], _values[i]);
        return d;
    }

#if NET9_0_OR_GREATER
    [BenchmarkCategory("Build")]
    [Benchmark]
    public BclOrdered Bcl_Add()
    {
        var d = new BclOrdered(capacity: N);
        for (int i = 0; i < N; i++)
            d.Add(_keys[i], _values[i]);
        return d;
    }
#endif

    [BenchmarkCategory("Build")]
    [Benchmark]
    public PengdowsOrdered Pengdows_TryAdd()
    {
        var d = new PengdowsOrdered(capacity: N);
        for (int i = 0; i < N; i++)
            d.TryAdd(_keys[i], _values[i]);
        return d;
    }

#if NET9_0_OR_GREATER
    [BenchmarkCategory("Build")]
    [Benchmark]
    public BclOrdered Bcl_TryAdd()
    {
        var d = new BclOrdered(capacity: N);
        for (int i = 0; i < N; i++)
            d.TryAdd(_keys[i], _values[i]);
        return d;
    }
#endif

    // ----------------------------
    // Lookup: hits
    // ----------------------------

    [BenchmarkCategory("LookupHit")]
    [Benchmark(Baseline = true)]
    public int Pengdows_TryGetValue_Hit()
    {
        int sum = 0;
        for (int i = 0; i < N; i++)
            if (_peng.TryGetValue(_keys[i], out var v) && v is int x) sum += x;
        return sum;
    }

#if NET9_0_OR_GREATER
    [BenchmarkCategory("LookupHit")]
    [Benchmark]
    public int Bcl_TryGetValue_Hit()
    {
        int sum = 0;
        for (int i = 0; i < N; i++)
            if (_bcl.TryGetValue(_keys[i], out var v) && v is int x) sum += x;
        return sum;
    }
#endif

    // ----------------------------
    // Lookup: misses
    // ----------------------------

    [BenchmarkCategory("LookupMiss")]
    [Benchmark(Baseline = true)]
    public int Pengdows_TryGetValue_Miss()
    {
        int misses = 0;
        for (int i = 0; i < N; i++)
            if (!_peng.TryGetValue(_missKeys[i], out _)) misses++;
        return misses;
    }

#if NET9_0_OR_GREATER
    [BenchmarkCategory("LookupMiss")]
    [Benchmark]
    public int Bcl_TryGetValue_Miss()
    {
        int misses = 0;
        for (int i = 0; i < N; i++)
            if (!_bcl.TryGetValue(_missKeys[i], out _)) misses++;
        return misses;
    }
#endif

    // ----------------------------
    // Enumeration (insertion order)
    // ----------------------------

    [BenchmarkCategory("Enumerate")]
    [Benchmark(Baseline = true)]
    public int Pengdows_Enumerate()
    {
        int sum = 0;
        foreach (var kv in _peng)
            if (kv.Value is int x) sum += x;
        return sum;
    }

#if NET9_0_OR_GREATER
    [BenchmarkCategory("Enumerate")]
    [Benchmark]
    public int Bcl_Enumerate()
    {
        int sum = 0;
        foreach (var kv in _bcl)
            if (kv.Value is int x) sum += x;
        return sum;
    }
#endif

    // ----------------------------
    // Remove half
    // ----------------------------

    [BenchmarkCategory("RemoveHalf")]
    [Benchmark(Baseline = true)]
    public int Pengdows_RemoveHalf()
    {
        var d = new PengdowsOrdered(capacity: N);
        for (int i = 0; i < N; i++) d.Add(_keys[i], _values[i]);

        int removed = 0;
        for (int i = 0; i < N; i += 2)
            if (d.Remove(_keys[i])) removed++;

        return removed;
    }

#if NET9_0_OR_GREATER
    [BenchmarkCategory("RemoveHalf")]
    [Benchmark]
    public int Bcl_RemoveHalf()
    {
        var d = new BclOrdered(capacity: N);
        for (int i = 0; i < N; i++) d.Add(_keys[i], _values[i]);

        int removed = 0;
        for (int i = 0; i < N; i += 2)
            if (d.Remove(_keys[i])) removed++;

        return removed;
    }
#endif

    // ----------------------------
    // Clear + rebuild (your Clear drops arrays)
    // ----------------------------

    [BenchmarkCategory("ClearRebuild")]
    [Benchmark(Baseline = true)]
    public int Pengdows_Clear_Rebuild()
    {
        var d = new PengdowsOrdered(capacity: N);
        for (int i = 0; i < N; i++) d.Add(_keys[i], _values[i]);

        d.Clear(); // aggressive release in your impl

        for (int i = 0; i < N; i++) d.Add(_keys[i], _values[i]);
        return d.Count;
    }

#if NET9_0_OR_GREATER
    [BenchmarkCategory("ClearRebuild")]
    [Benchmark]
    public int Bcl_Clear_Rebuild()
    {
        var d = new BclOrdered(capacity: N);
        for (int i = 0; i < N; i++) d.Add(_keys[i], _values[i]);

        d.Clear(); // BCL likely keeps arrays

        for (int i = 0; i < N; i++) d.Add(_keys[i], _values[i]);
        return d.Count;
    }
#endif
}
