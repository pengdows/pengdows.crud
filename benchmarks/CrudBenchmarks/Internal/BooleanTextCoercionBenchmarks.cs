using BenchmarkDotNet.Attributes;
using CrudBenchmarks;

namespace CrudBenchmarks.Internal;

// Reads boolean text through the core coercion path. Tokens are chosen so that every implementation
// returns a value, so a timing never includes an exception.
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class BooleanTextCoercionBenchmarks
{
    [Benchmark(Baseline = true)]
    public object? TrueText() => pengdows.crud.TypeCoercionHelper.Coerce("true", typeof(string), typeof(bool));

    [Benchmark]
    public object? FalseText() => pengdows.crud.TypeCoercionHelper.Coerce("false", typeof(string), typeof(bool));

    [Benchmark]
    public object? ZeroText() => pengdows.crud.TypeCoercionHelper.Coerce("0", typeof(string), typeof(bool));

    [Benchmark]
    public object? FalseTokenF() => pengdows.crud.TypeCoercionHelper.Coerce("f", typeof(string), typeof(bool));

    [Benchmark]
    public object? UpperCaseTrue() => pengdows.crud.TypeCoercionHelper.Coerce("TRUE", typeof(string), typeof(bool));
}
