using System;
using System.Collections.Generic;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.@internal;

namespace CrudBenchmarks.Internal;

[MemoryDiagnoser]
public class ConnectionStringNormalizationBenchmarks
{
    private delegate bool TryNormalize(
        string connectionString,
        string? readOnlyKey,
        string? readOnlyValue,
        string? applicationNameSettingName,
        string readOnlySuffix,
        out Dictionary<string, string> normalized);

    private const string ConnectionString = "Server=test;Database=benchmark;User Id=app;Password=secret;Application Name=tracing:ro";
    private readonly TryNormalize _normalize;

    public ConnectionStringNormalizationBenchmarks()
    {
        var method = typeof(DatabaseContext).GetMethod(
            "TryBuildNormalizedConnectionMap",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null)
        {
            throw new InvalidOperationException("Normalization helper not found.");
        }

        _normalize = (TryNormalize)method.CreateDelegate(typeof(TryNormalize))!;
    }

    [IterationSetup(Target = nameof(Normalize_NoCache))]
    public void SetupNoCache()
    {
        ConnectionStringNormalizationCache.ClearForTests();
    }

    [IterationSetup(Target = nameof(Normalize_Cached))]
    public void SetupCached()
    {
        ConnectionStringNormalizationCache.ClearForTests();
        _normalize(ConnectionString, null, null, null, ":ro", out _);
    }

    [Benchmark(Baseline = true)]
    public Dictionary<string, string> Normalize_NoCache()
    {
        _normalize(ConnectionString, null, null, null, ":ro", out var normalized);
        return normalized;
    }

    [Benchmark]
    public Dictionary<string, string> Normalize_Cached()
    {
        _normalize(ConnectionString, null, null, null, ":ro", out var normalized);
        return normalized;
    }
}
