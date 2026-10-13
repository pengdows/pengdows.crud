using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using System.Threading.Channels;
using Dapper;
using Microsoft.Data.Sqlite;

namespace CrudBenchmarks;

/// <summary>
/// A throwaway file-backed SQLite database in WAL mode, the shape most applications run: a real file, a commit that
/// reaches the OS on every write. The shared-cache memory database the other SQLite classes use has no disk commit, so
/// it shows the worst case for per-operation overhead; this one shows what a write costs when it is a real write.
///
/// WHERE THE FILE LIVES MATTERS. The default is the OS temp directory, which is RAM (tmpfs) on many Linux machines;
/// a commit to RAM costs microseconds and tells you almost nothing a memory database doesn't. Set
/// CRUD_BENCH_DB_DIR to a directory on the disk you mean to measure, and read the path this prints.
/// </summary>
internal sealed class SqliteFileDatabase : IDisposable
{
    internal const int SeedRows = 1000;

    public string Path { get; }

    public SqliteFileDatabase()
    {
        var directory = Environment.GetEnvironmentVariable("CRUD_BENCH_DB_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = System.IO.Path.GetTempPath();
        }
        else
        {
            Directory.CreateDirectory(directory);
        }

        Path = System.IO.Path.Combine(directory, $"pengdows_sqlite_file_bench_{Guid.NewGuid():N}.db");
        Console.WriteLine($"[SqliteFileDatabase] {Path}");
        using var connection = new SqliteConnection($"Data Source={Path};Pooling=False");
        connection.Open();
        connection.Execute("PRAGMA journal_mode = WAL; CREATE TABLE stress_test (id INTEGER PRIMARY KEY, value INTEGER NOT NULL);");
        using var transaction = connection.BeginTransaction();
        for (var i = 1; i <= SeedRows; i++)
        {
            connection.Execute("INSERT INTO stress_test (id, value) VALUES (@id, @value)", new { id = i, value = i * 10 }, transaction);
        }

        transaction.Commit();
    }

    private static string Timeout(int? busyTimeoutMs) =>
        busyTimeoutMs is { } ms ? $";Default Timeout={SQLiteWriteContentionBenchmarks.CommandTimeoutSecondsFor(ms)}" : string.Empty;

    /// <summary>
    /// What a pengdows.crud checkout runs on a WAL database, as one command: foreign keys on, query_only off,
    /// synchronous NORMAL. The per-operation Dapper arms run it on every open and the pinned, queue and single-connection
    /// arms once, so every arm does the same work and none gets a durability advantage (NORMAL in WAL keeps every committed
    /// transaction through an application crash; FULL syncs the log on every commit).
    /// </summary>
    public const string SessionSql = "PRAGMA foreign_keys = ON; PRAGMA query_only = OFF; PRAGMA synchronous = NORMAL;";

    /// <summary>The Dapper arms' connection string. The session settings go in <see cref="SessionSql"/>, not in
    /// keywords: Microsoft.Data.Sqlite has a keyword for foreign keys only, and a second command per open would
    /// make the Dapper arms do more than pengdows.crud's one.</summary>
    public string DapperConnectionString(int? busyTimeoutMs = null) => $"Data Source={Path}{Timeout(busyTimeoutMs)}";

    /// <summary>Opens a connection and runs <see cref="SessionSql"/> on it: a per-operation Dapper arm's open.</summary>
    public static async Task<SqliteConnection> OpenWithSessionAsync(string connectionString, string? extraSql = null)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(extraSql is null ? SessionSql : $"{SessionSql} {extraSql}");
        return connection;
    }

    /// <summary>The pengdows.crud context's connection string: pengdows.crud sets the pragma itself.</summary>
    public string PengdowsConnectionString(int? busyTimeoutMs = null) => $"Data Source={Path}{Timeout(busyTimeoutMs)}";

    public static string BusyTimeoutSql(int busyTimeoutMs) => $"PRAGMA busy_timeout={busyTimeoutMs};";

    public async Task<int> ValueAsync(int id)
    {
        await using var connection = new SqliteConnection(DapperConnectionString());
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT value FROM stress_test WHERE id = @id", new { id });
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(Path + suffix);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }
}

/// <summary>
/// The pinned-writer pattern: one long-lived connection and a gate, so one write runs at a time. This is what
/// sqlite-net-pcl, GRDB and a size-1 writer pool amount to once they are running.
/// </summary>
internal sealed class PinnedWriterArm : IDisposable
{
    private readonly Func<DbConnection> _open;
    private readonly string? _setupSql;

    public PinnedWriterArm(Func<DbConnection> open, string? setupSql = null)
    {
        _open = open;
        _setupSql = setupSql;
        Connection = OpenNew();
    }

    public PinnedWriterArm(string connectionString, string? setupSql = null)
        : this(() => new SqliteConnection(connectionString), setupSql)
    {
    }

    public SemaphoreSlim Gate { get; } = new(1, 1);

    public DbConnection Connection { get; private set; }

    private DbConnection OpenNew()
    {
        var connection = _open();
        connection.Open();
        if (_setupSql != null)
        {
            connection.Execute(_setupSql);
        }

        return connection;
    }

    public async Task<T> RunAsync<T>(Func<DbConnection, Task<T>> work)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await work(Connection).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>The least a careful hand-rolled writer needs after a failure: replace the connection.</summary>
    public void Reconnect()
    {
        Gate.Wait();
        try
        {
            Connection.Dispose();
            Connection = OpenNew();
        }
        finally
        {
            Gate.Release();
        }
    }

    public void Dispose()
    {
        Connection.Dispose();
        Gate.Dispose();
    }
}

/// <summary>
/// The writer-queue pattern: callers enqueue work and one background task, holding one connection, runs it in order.
/// This is the shape of EntityFrameworkCore.Sqlite.Concurrency and PepperX.SqliteGate: a channel drained by a single
/// writer, each request completed through a TaskCompletionSource.
/// </summary>
internal sealed class WriterQueueArm : IAsyncDisposable
{
    private readonly Channel<IWork> _channel = Channel.CreateUnbounded<IWork>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SqliteConnection _connection;
    private readonly Task _loop;

    public WriterQueueArm(string connectionString, string? setupSql = null)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        if (setupSql != null)
        {
            _connection.Execute(setupSql);
        }

        _loop = Task.Run(LoopAsync);
    }

    public Task<T> EnqueueAsync<T>(Func<SqliteConnection, Task<T>> work)
    {
        var item = new Work<T>(work);
        if (!_channel.Writer.TryWrite(item))
        {
            throw new InvalidOperationException("The writer queue is closed.");
        }

        return item.Result;
    }

    private async Task LoopAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            await item.RunAsync(_connection).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.Complete();
        await _loop.ConfigureAwait(false);
        _connection.Dispose();
    }

    private interface IWork
    {
        Task RunAsync(SqliteConnection connection);
    }

    private sealed class Work<T> : IWork
    {
        private readonly Func<SqliteConnection, Task<T>> _work;
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Work(Func<SqliteConnection, Task<T>> work) => _work = work;

        public Task<T> Result => _completion.Task;

        public async Task RunAsync(SqliteConnection connection)
        {
            try
            {
                _completion.SetResult(await _work(connection).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _completion.SetException(ex);
            }
        }
    }
}

/// <summary>Per-operation latencies for one arm, summarized as percentiles in a sidecar file next to the results.</summary>
internal sealed class LatencyLog
{
    private readonly ConcurrentBag<long> _ticks = new();

    public void Add(long ticks) => _ticks.Add(ticks);

    public int Count => _ticks.Count;

    public static void AppendSection(StringBuilder sb, string title, LatencyLog log)
    {
        var sorted = log._ticks.ToArray();
        Array.Sort(sorted);
        sb.AppendLine($"### {title} ({sorted.Length} samples)");
        sb.AppendLine();
        if (sorted.Length == 0)
        {
            sb.AppendLine("_none recorded_");
            sb.AppendLine();
            return;
        }

        static string Ms(double ticks) => $"{ticks / System.Diagnostics.Stopwatch.Frequency * 1000.0:F3} ms";
        sb.AppendLine("| P50 | P95 | P99 | Max |");
        sb.AppendLine("|---:|---:|---:|---:|");
        sb.AppendLine($"| {Ms(PercentileMath.NearestRank(sorted, 50))} | {Ms(PercentileMath.NearestRank(sorted, 95))} | " +
                      $"{Ms(PercentileMath.NearestRank(sorted, 99))} | {Ms(sorted[^1])} |");
        sb.AppendLine();
    }

    public static void WriteSidecar(string fileName, string content)
    {
        try
        {
            var dir = Environment.GetEnvironmentVariable("CRUD_BENCH_ARTIFACTS_DIR")
                      ?? System.IO.Path.Combine("BenchmarkDotNet.Artifacts", "results");
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, fileName);
            File.WriteAllText(path, content);
            Console.WriteLine($"[LatencyLog] Wrote {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LatencyLog] Failed to write {fileName}: {ex.Message}");
        }
    }
}
