using System.Data;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace CrudBenchmarks;

/// <summary>
/// More threads than the pool has slots, each running short transactions through the library's blocking
/// <c>BeginTransaction</c>: the workload that reaches the pool governor's blocking acquire. Every thread beyond
/// the slot count queues, and every queued caller blocks its thread. SQLite cannot show this, because
/// SingleWriter serializes writers on a separate turnstile before the gate is ever reached; a pool of several
/// slots on a real server can.
/// <see cref="ThinkMicroseconds"/> is the time a caller spends away from the pool between transactions: 0
/// re-acquires immediately, the pattern that favors a primitive that lets the releasing thread take its permit
/// back; tens of microseconds is closer to a real caller.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 8)]
public class PostgreSqlSyncPoolStormBenchmarks
{
    private const int Threads = 32;

    [Params(2, 8)]
    public int Slots { get; set; }

    [Params(0, 100)]
    public int ThinkMicroseconds { get; set; }

    private IContainer _container = null!;
    private SyncPoolStorm _storm = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _container = new ContainerBuilder()
            .WithImage("postgres:16-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "storm")
            .WithEnvironment("POSTGRES_DB", "storm")
            .WithCommand("-c", "max_connections=200")
            .WithPortBinding(0, 5432)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();
        await _container.StartAsync();

        var port = _container.GetMappedPublicPort(5432);
        var connectionString =
            $"Host=localhost;Port={port};Database=storm;Username=postgres;Password=storm;Pooling=true;Maximum Pool Size=100";
        _storm = new SyncPoolStorm(new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            MaxConcurrentReads = Slots,
            MaxConcurrentWrites = Slots,
#if !BASELINE_2X
            MaxQueuedReads = 1_024,
#endif
            PoolAcquireTimeout = TimeSpan.FromMinutes(2),
            EnableMetrics = true
        }, NpgsqlFactory.Instance));
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        var waits = _storm.Context.GetPoolStatisticsSnapshot(PoolLabel.Reader);
#if !BASELINE_2X
        Console.WriteLine(
            $"// SyncPoolStorm Slots={Slots} Think={ThinkMicroseconds}us: acquired={waits.TotalAcquired} waits={waits.TotalWaits} peakQueued={waits.PeakQueued}");
#else
        Console.WriteLine(
            $"// SyncPoolStorm Slots={Slots} Think={ThinkMicroseconds}us: acquired={waits.TotalAcquired} peakQueued={waits.PeakQueued}");
#endif
        _storm.Dispose();
        await _container.DisposeAsync();
    }

    // Each invocation is enough round trips to dwarf thread start-up.
    [Benchmark]
    public int SyncStorm() => _storm.Run(Threads, transactionsPerThread: 60, holdMicroseconds: 0, ThinkMicroseconds);
}

/// <summary>The storm itself, independent of the database it runs against.</summary>
internal sealed class SyncPoolStorm : IDisposable
{
    internal DatabaseContext Context { get; }

    internal SyncPoolStorm(DatabaseContext context) => Context = context;

    private int _concurrent;
    private int _peakConcurrent;

    /// <summary>The most transactions ever open at once, counted inside them.</summary>
    internal int PeakConcurrentTransactions => Volatile.Read(ref _peakConcurrent);

    /// <summary>A storm against fakeDb, for tests: no server, same library path.</summary>
    internal static SyncPoolStorm OnFakeDb(int slots) => new(new DatabaseContext(new DatabaseContextConfiguration
    {
        ConnectionString = "Host=fake;Database=storm;EmulatedProduct=PostgreSql",
        MaxConcurrentReads = slots,
        MaxConcurrentWrites = slots,
#if !BASELINE_2X
        MaxQueuedReads = 1_024,
#endif
        PoolAcquireTimeout = TimeSpan.FromMinutes(1),
        EnableMetrics = true
    }, new fakeDbFactory(SupportedDatabase.PostgreSql)));

    /// <summary>
    /// Releases <paramref name="threads"/> threads together; each commits <paramref name="transactionsPerThread"/>
    /// short read transactions through the blocking API, keeping the slot a further
    /// <paramref name="holdMicroseconds"/> inside each (a real server's round trips supply the rest) and spending
    /// <paramref name="thinkMicroseconds"/> away from the pool between them. Returns how many committed.
    /// </summary>
    internal int Run(int threads, int transactionsPerThread, int holdMicroseconds, int thinkMicroseconds)
    {
        var committed = 0;
        using var ready = new CountdownEvent(threads);
        using var start = new ManualResetEventSlim(false);
        var workers = new Thread[threads];
        Exception? failure = null;

        for (var t = 0; t < threads; t++)
        {
            workers[t] = new Thread(() =>
            {
                try
                {
                    ready.Signal();
                    start.Wait();
                    for (var i = 0; i < transactionsPerThread; i++)
                    {
                        RunTransaction(holdMicroseconds);
                        Interlocked.Increment(ref committed);
                        Spin(thinkMicroseconds);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref failure, ex, null);
                }
            }) { IsBackground = true };
            workers[t].Start();
        }

        ready.Wait();
        start.Set();
        foreach (var worker in workers)
        {
            worker.Join();
        }

        if (failure != null)
        {
            throw new InvalidOperationException("A storm thread failed.", failure);
        }

        return committed;
    }

    private void RunTransaction(int holdMicroseconds)
    {
        using var transaction = Context.BeginTransaction(null, ExecutionType.Read);
        var now = Interlocked.Increment(ref _concurrent);
        int seen;
        while (now > (seen = Volatile.Read(ref _peakConcurrent)) &&
               Interlocked.CompareExchange(ref _peakConcurrent, now, seen) != seen)
        {
        }

        using var container = transaction.CreateSqlContainer("SELECT 1");
        container.ExecuteScalarOrNullAsync<int>().AsTask().GetAwaiter().GetResult();
        Spin(holdMicroseconds);
        Interlocked.Decrement(ref _concurrent);
        transaction.Commit();
    }

    private static void Spin(int microseconds)
    {
        if (microseconds <= 0)
        {
            return;
        }

        var end = Stopwatch.GetTimestamp() + (microseconds * Stopwatch.Frequency / 1_000_000);
        while (Stopwatch.GetTimestamp() < end)
        {
            Thread.SpinWait(1);
        }
    }

    public void Dispose() => Context.Dispose();
}
