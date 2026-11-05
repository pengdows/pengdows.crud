using System.Diagnostics.Metrics;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using CrudBenchmarks;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.metrics;
using pengdows.crud.opentelemetry;

namespace CrudBenchmarks.Internal;

// Isolates the observer's per-event handler. The handler is invoked directly with a snapshot that has
// already been exported, so the measured cost is the baseline comparison and the per-event lock or
// staleness check, not command execution.
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MetricsObserverEventBenchmarks
{
    private DatabaseContext? _context;
    private PengdowsMetricsObserver? _observer;
    private Action<object?, DatabaseMetrics>? _handler;
    private DatabaseMetrics _snapshot;

    [GlobalSetup]
    public void Setup()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true,
            ApplicationName = "ObserverBench"
        };
        _context = new DatabaseContext(config, factory);
        _observer = new PengdowsMetricsObserver();
        _observer.Track(_context);

        var method = typeof(PengdowsMetricsObserver).GetMethod(
            "HandleMetricsUpdated", BindingFlags.NonPublic | BindingFlags.Instance)!;
        _handler = (Action<object?, DatabaseMetrics>)method.CreateDelegate(
            typeof(Action<object?, DatabaseMetrics>), _observer);
        _snapshot = _context.Metrics;
    }

    [Benchmark]
    public void HandleUnchangedSnapshot() => _handler!(_context, _snapshot);

    [GlobalCleanup]
    public void Cleanup()
    {
        _observer?.Dispose();
        _context?.Dispose();
    }
}
