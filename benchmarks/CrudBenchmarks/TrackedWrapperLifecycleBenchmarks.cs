using System.Data.Common;
using BenchmarkDotNet.Attributes;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;

namespace CrudBenchmarks;

/// <summary>
/// What every query pays around its real work, with no metrics collector (the default): a tracked reader built, read
/// and disposed, and a tracked connection opened and closed. No database is involved, so what is timed is the wrapper
/// code itself, and what a change to it must not slow down.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 15)]
public class TrackedWrapperLifecycleBenchmarks
{
    private static readonly Dictionary<string, object>[] OneRow = { new() { ["id"] = 1, ["name"] = "a name" } };

    private TrackedConnection _connection = null!;
    private readonly IAsyncDisposable _locker = new NoopDisposable();

    [GlobalSetup]
    public void Setup() => _connection = new TrackedConnection(new fakeDbConnection());

    [GlobalCleanup]
    public void Cleanup() => _connection.Dispose();

    [Benchmark]
    public int ReaderLifecycle()
    {
        var reader = new TrackedReader(new fakeDbDataReader(OneRow), _connection, _locker, false);
        var rows = 0;
        while (reader.Read())
        {
            rows++;
        }

        reader.Dispose();
        return rows;
    }

    [Benchmark]
    public bool ConnectionLifecycle()
    {
        using var connection = new TrackedConnection(new fakeDbConnection());
        connection.Open();
        connection.Close();
        return connection.State == System.Data.ConnectionState.Closed;
    }

    private sealed class NoopDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
