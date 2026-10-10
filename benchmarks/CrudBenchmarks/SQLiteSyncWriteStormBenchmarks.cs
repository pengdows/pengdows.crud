using System.Data;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Many threads, one writer slot, short transactions, the synchronous API: the workload that reaches the pool
/// governor's blocking acquire, which the asynchronous write storm never touches. SQLite's SingleWriter mode
/// gives every write a single slot, so every transaction beyond the first queues and every queued caller blocks
/// its thread. <see cref="ThinkMicroseconds"/> is the time a caller spends away from the pool between
/// transactions: 0 is a caller that re-acquires immediately, the pattern that favors a primitive that lets the
/// releasing thread take its permit back; a few tens of microseconds is closer to a real caller.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 8)]
public class SQLiteSyncWriteStormBenchmarks
{
    private const int Threads = 16;

    [Params(0, 20, 100)]
    public int ThinkMicroseconds { get; set; }

    private SyncWriteStorm _storm = null!;

    // A transaction is tens of microseconds, so each invocation runs enough of them to dwarf thread start-up.
    private int TransactionsPerThread => ThinkMicroseconds switch { 0 => 500, 20 => 400, _ => 200 };

    [GlobalSetup]
    public void Setup() => _storm = CreateStorm();

    [GlobalCleanup]
    public void Cleanup() => _storm.Dispose();

    [Benchmark]
    public int SyncWriteStorm() => _storm.Run(Threads, TransactionsPerThread, ThinkMicroseconds);

    internal static SyncWriteStorm CreateStorm() => new();
}

internal sealed class SyncWriteStorm : IDisposable
{
    private readonly SqliteConnection _sentinel;
    private int _nextId;

    internal DatabaseContext Context { get; }

    internal SyncWriteStorm()
    {
        var connectionString = $"Data Source=sync_storm_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";

        // The sentinel keeps the shared in-memory database alive between connections.
        _sentinel = new SqliteConnection(connectionString);
        _sentinel.Open();
        using (var create = _sentinel.CreateCommand())
        {
            create.CommandText = "CREATE TABLE storm (id INTEGER PRIMARY KEY, value INTEGER NOT NULL);";
            create.ExecuteNonQuery();
        }

        // SQLite is forced to SingleWriter by its dialect, which gives writers one slot. The queue cap is raised
        // above the caller count so a benchmark of waiting is never measuring a rejection.
        Context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            PoolAcquireTimeout = TimeSpan.FromMinutes(2),
#if !BASELINE_2X
            MaxQueuedWrites = 1_024,
#endif
            EnableMetrics = true
        }, SqliteFactory.Instance);
    }

    /// <summary>
    /// Releases <paramref name="threads"/> threads together; each commits <paramref name="transactionsPerThread"/>
    /// one-row transactions through the synchronous API, spending <paramref name="thinkMicroseconds"/> away from
    /// the pool between them. Returns how many committed.
    /// </summary>
    internal int Run(int threads, int transactionsPerThread, int thinkMicroseconds)
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
                        WriteOne();
                        Interlocked.Increment(ref committed);
                        Think(thinkMicroseconds);
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

    private void WriteOne()
    {
        var id = Interlocked.Increment(ref _nextId);
        using var transaction = Context.BeginTransaction();
        using var container = transaction.CreateSqlContainer();
        container.Query.Append("INSERT INTO storm (id, value) VALUES (");
        container.Query.Append(container.MakeParameterName("id"));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName("value"));
        container.Query.Append(')');
        container.AddParameterWithValue("id", DbType.Int32, id);
        container.AddParameterWithValue("value", DbType.Int32, id);
        container.ExecuteNonQueryAsync().AsTask().GetAwaiter().GetResult();
        transaction.Commit();
    }

    // Time spent away from the pool: a spin, so the thread stays runnable like a caller doing real work.
    private static void Think(int microseconds)
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

    internal int WritesRecorded()
    {
        using var count = _sentinel.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM storm;";
        return Convert.ToInt32(count.ExecuteScalar());
    }

    public void Dispose()
    {
        Context.Dispose();
        _sentinel.Dispose();
    }
}
