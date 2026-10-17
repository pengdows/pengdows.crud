using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace CrudBenchmarks;

/// <summary>
/// Per-column-type profile of row hydration: where FakeDbHydrationBenchmarks' whole-row gap to Dapper actually
/// goes. For each column type a single-column entity is mapped by Dapper (the baseline), a hand-written mapper,
/// pengdows.crud's CompiledMapperFactory (TableGateway path), DataReaderMapper's resolved plan (arbitrary-POCO
/// path), and the bare reader read as a floor. Same rows, same sync loop, mapping resolved once in setup, so only
/// the mapper varies. fakeDb getters are cheap, which makes this a lower-bound CPU microscope.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class FakeDbColumnHydrationBenchmarks
{
    private ColumnCase _case = null!;

    [ParamsSource(nameof(ColumnNames))]
    public string Column { get; set; } = "Int32";

    public int RowCount { get; set; } = 1000;

    /// <summary>Every column type; BENCH_COLUMNS=PgRangeInt,PgCidr narrows a run to those (a filter can't select a parameter).</summary>
    public static IEnumerable<string> ColumnNames()
    {
        var all = ColumnCases.Names;
        var only = Environment.GetEnvironmentVariable("BENCH_COLUMNS");
        if (string.IsNullOrWhiteSpace(only))
        {
            return all;
        }

        var wanted = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return all.Where(c => wanted.Contains(c, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _case = ColumnCases.Create(Column, RowCount);
        var mismatch = _case.Verify();
        if (mismatch != null)
        {
            throw new InvalidOperationException($"{Column}: {mismatch}");
        }
    }

    [Benchmark(Baseline = true)]
    public int Dapper() => _case.RunDapper();

    [Benchmark]
    public int Manual() => _case.RunManual();

    [Benchmark]
    public int CompiledMapper() => _case.RunCompiled();

    [Benchmark]
    public int ReaderMapperPlan() => _case.RunPlan();

    [Benchmark]
    public int ReadFloor() => _case.RunFloor();
}
