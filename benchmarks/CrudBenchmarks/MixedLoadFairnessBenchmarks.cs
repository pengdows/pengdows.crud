using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.SqlClient;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 3)]
public class MixedLoadFairnessBenchmarks : IAsyncDisposable
{
    private const int ReaderConcurrency = 8;
    private const int DurationSeconds = 6;

    private static readonly IndexedViewEnvironment Environment = new();

    private DatabaseContext? _context;
    private readonly List<MixedLoadResult> _results = new();

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        await Environment.InitializeAsync();
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = Environment.ConnectionString + "Application Name=Benchmark_MixedLoad;",
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard,
            EnablePoolGovernor = true,
            MaxConcurrentReads = ReaderConcurrency,
            MaxConcurrentWrites = 1,
            PoolAcquireTimeout = TimeSpan.FromSeconds(10)
        };

        _context = new DatabaseContext(cfg, SqlClientFactory.Instance, null, new TypeMapRegistry());
    }

    [GlobalCleanup]
    public async ValueTask DisposeAsync()
    {
        if (_context is IAsyncDisposable candidate)
        {
            await candidate.DisposeAsync();
        }
    }

    [Benchmark]
    public async Task MixedLoad_FairnessRun()
    {
        if (_context == null)
        {
            throw new InvalidOperationException("Database context was not initialized.");
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DurationSeconds));
        var writerWaits = new List<TimeSpan>();
        var writerExecs = new List<TimeSpan>();
        var readCounter = 0L;
        var writerCounter = 0L;

        async Task ReaderLoop()
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var tracked = _context.GetConnection(ExecutionType.Read);
                    using var command = (DbCommand)tracked.CreateCommand();
                    command.CommandText = "SELECT 1";
                    await command.ExecuteScalarAsync(cts.Token);
                    _context.CloseAndDisposeConnection(tracked);
                    Interlocked.Increment(ref readCounter);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the cancellation token fires.
            }
        }

        async Task WriterLoop()
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var waitSw = Stopwatch.StartNew();
                    var tracked = _context.GetConnection(ExecutionType.Write);
                    waitSw.Stop();
                    writerWaits.Add(waitSw.Elapsed);

                    using var command = (DbCommand)tracked.CreateCommand();
                    command.CommandText = "UPDATE dbo.Customers SET created_date = created_date WHERE customer_id = @customerId";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@customerId";
                    parameter.Value = Environment.SampleCustomerId;
                    command.Parameters.Add(parameter);

                    var execSw = Stopwatch.StartNew();
                    await command.ExecuteNonQueryAsync(cts.Token);
                    execSw.Stop();
                    writerExecs.Add(execSw.Elapsed);

                    writerCounter++;
                    _context.CloseAndDisposeConnection(tracked);
                    await Task.Delay(10, cts.Token).ContinueWith(_ => { }, TaskScheduler.Default);
                }
            }
            catch (OperationCanceledException)
            {
                // expected when cancellation fires
            }
        }

        var readers = Enumerable.Range(0, ReaderConcurrency)
            .Select(_ => Task.Run(ReaderLoop, cts.Token))
            .ToList();
        var writer = Task.Run(WriterLoop, cts.Token);

        await Task.WhenAll(readers.Append(writer));

        var writerSnapshot = _context.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        var readerSnapshot = _context.GetPoolStatisticsSnapshot(PoolLabel.Reader);

        var result = new MixedLoadResult(
            Percentile(writerWaits, 0.5),
            Percentile(writerWaits, 0.95),
            Percentile(writerWaits, 0.99),
            Percentile(writerExecs, 0.95),
            readCounter,
            writerCounter,
            writerSnapshot,
            readerSnapshot);

        _results.Add(result);

        Console.WriteLine($"[Benchmark] Writer wait p95={result.WriterWaitP95:F2}ms exec p95={result.WriterExecP95:F2}ms reads={result.ReadCount}, writer snapshot={result.WriterSnapshot}");
    }

    private static double Percentile(IEnumerable<TimeSpan> samples, double percentile)
    {
        var list = samples.Select(sample => sample.TotalMilliseconds).Where(value => !double.IsNaN(value)).ToList();
        if (list.Count == 0)
        {
            return 0d;
        }

        list.Sort();
        var position = Math.Clamp((list.Count - 1) * percentile, 0, list.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return list[lower];
        }

        var weight = position - lower;
        return list[lower] * (1 - weight) + list[upper] * weight;
    }

    private readonly record struct MixedLoadResult(
        double WriterWaitP50,
        double WriterWaitP95,
        double WriterWaitP99,
        double WriterExecP95,
        long ReadCount,
        long WriterCount,
        PoolStatisticsSnapshot WriterSnapshot,
        PoolStatisticsSnapshot ReaderSnapshot);
}
