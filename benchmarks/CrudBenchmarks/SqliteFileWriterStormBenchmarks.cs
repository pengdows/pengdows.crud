using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// FAULT INJECTION AND TAIL LATENCY, NOT A SPEED CLAIM. What happens against a real WAL file when writers collide,
/// for each way of serializing SQLite writes (see SqliteFileWriterBenchmarks for the arms). Two questions:
///
///   Storm_*  - 100 concurrent writers, each a transaction of 50 updates. Failed transactions are counted per arm
///              (the Fails column). A pinned writer, a writer queue and pengdows.crud all serialize, so none of them
///              fails here; what separates them is the time a writer waits (read the latency sidecar, P99 and max).
///              The plain Dapper arm has no coordination and races for SQLite's write lock.
///   Mixed_*  - writers doing single-row commits while readers read continuously. Writer and reader latency are
///              recorded separately. A single connection behind one gate makes readers queue behind writers; a pinned
///              writer or a queue leaves readers alone; pengdows.crud's writer turnstile keeps writers from being
///              starved by the readers.
///
/// Never quote the time of an arm that failed transactions as a speed: a failed transaction ends early.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 5)]
public class SqliteFileWriterStormBenchmarks : IDisposable
{
    private const string UpdateSql = "UPDATE stress_test SET value = @v WHERE id = @id";
    private const string ReadSql = "SELECT value FROM stress_test WHERE id = @id";
    private const string ScenarioStorm = "Storm";
    private const string ScenarioMixed = "Mixed";

    private const string FrameworkDapper = "Dapper";
    private const string FrameworkPinned = "DapperPinnedWriter";
    private const string FrameworkSingle = "DapperSingleConnection";
    private const string FrameworkQueue = "DapperWriterQueue";
    private const string FrameworkPengdows = "Pengdows";
    private const string FrameworkPengdowsDefaults = "PengdowsDefaults";
    private const string FrameworkPengdowsFairnessOff = "PengdowsFairnessOff";

    /// <summary>10 is a deliberately narrow stress setting, 5000 a sane one; read both rows together.</summary>
    [Params(10, 5000)] public int BusyTimeoutMs { get; set; }

    public int StormConcurrency { get; set; } = 100;
    public int WritesPerTransaction { get; set; } = 50;
    public int MixedWriters { get; set; } = 20;
    public int WritesPerWriter { get; set; } = 20;
    public int MixedReaders { get; set; } = 16;

    private SqliteFileDatabase _database = null!;
    private string _dapperConnectionString = null!;
    private PinnedWriterArm _pinned = null!;
    private PinnedWriterArm _single = null!;
    private WriterQueueArm _queue = null!;
    private DatabaseContext _pengdows = null!;
    private DatabaseContext _pengdowsDefaults = null!;
    private DatabaseContext _pengdowsFairnessOff = null!;

    private readonly ConcurrentDictionary<CorrectnessIssueKey, int> _issues = new();
    private readonly LatencyLog _transactionLatency = new();
    private readonly LatencyLog _failedTransactionLatency = new();
    private readonly LatencyLog _writeLatency = new();
    private readonly LatencyLog _readLatency = new();
    private string _arm = "Unknown";
    private bool _wasMixed;

    private int _committedTransactions;
    private int _failedTransactions;
    private int _committedWrites;
    private int _failedWrites;
    private int _completedReads;
    private volatile bool _writersDone;

    internal int CommittedTransactions => Volatile.Read(ref _committedTransactions);
    internal int FailedTransactions => Volatile.Read(ref _failedTransactions);
    internal int CommittedWrites => Volatile.Read(ref _committedWrites);
    internal int FailedWrites => Volatile.Read(ref _failedWrites);
    internal int CompletedReads => Volatile.Read(ref _completedReads);

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _database = new SqliteFileDatabase();
        _dapperConnectionString = _database.DapperConnectionString(BusyTimeoutMs);
        var setup = $"{SqliteFileDatabase.SessionSql} {SqliteFileDatabase.BusyTimeoutSql(BusyTimeoutMs)}";
        _pinned = new PinnedWriterArm(_dapperConnectionString, setup);
        _single = new PinnedWriterArm(_dapperConnectionString, setup);
        _queue = new WriterQueueArm(_dapperConnectionString, setup);

        var typeMap = new TypeMapRegistry();
        // The queue is sized for the storm on purpose: admission control otherwise rejects a 100-writer burst by
        // design (see SQLiteWriteContentionBenchmarks); the defaults arm below records what an unconfigured context does.
        _pengdows = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = _database.PengdowsConnectionString(BusyTimeoutMs),
            DbMode = DbMode.Best,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            PoolAcquireTimeout = TimeSpan.FromMinutes(5),
            MaxQueuedWrites = Math.Max(StormConcurrency, MixedWriters) + 8
        }, SqliteFactory.Instance, null, typeMap);
        // SQLITE-008: the same context as above with the writer turnstile off, to see what fairness costs readers on a WAL file.
        _pengdowsFairnessOff = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = _database.PengdowsConnectionString(BusyTimeoutMs),
            DbMode = DbMode.Best,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            PoolAcquireTimeout = TimeSpan.FromMinutes(5),
            MaxQueuedWrites = Math.Max(StormConcurrency, MixedWriters) + 8,
            EnableSingleWriterFairness = false
        }, SqliteFactory.Instance, null, typeMap);
        _pengdowsDefaults = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = _database.PengdowsConnectionString(BusyTimeoutMs),
            DbMode = DbMode.Best,
            ReadWriteMode = ReadWriteMode.ReadWrite
        }, SqliteFactory.Instance, null, typeMap);

        if (_pengdows.ConnectionMode != DbMode.SingleWriter || _pengdowsFairnessOff.ConnectionMode != DbMode.SingleWriter)
        {
            throw new InvalidOperationException(
                $"The pengdows arms must run in SingleWriter; they are in {_pengdows.ConnectionMode} and {_pengdowsFairnessOff.ConnectionMode}.");
        }

        await Task.CompletedTask;
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        BenchmarkCorrectnessArtifacts.Write(nameof(SqliteFileWriterStormBenchmarks),
            _issues
                .OrderBy(p => p.Key.ParameterKey, StringComparer.Ordinal)
                .ThenBy(p => p.Key.Scenario, StringComparer.Ordinal)
                .ThenBy(p => p.Key.Framework, StringComparer.Ordinal)
                .ThenBy(p => p.Key.Reason, StringComparer.Ordinal)
                .Select(p => new CorrectnessIssue(p.Key.ParameterKey, p.Key.Scenario, p.Key.Framework, p.Key.Reason, p.Value))
                .ToArray());

        WriteSidecar();

        _pengdowsFairnessOff?.Dispose();
        _pengdowsDefaults?.Dispose();
        _pengdows?.Dispose();
        _queue?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _single?.Dispose();
        _pinned?.Dispose();
        _database?.Dispose();
    }

    public void Dispose() => GlobalCleanup();

    private void WriteSidecar()
    {
        if (_arm == "Unknown")
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# SqliteFileWriterStormBenchmarks - {_arm} (busy_timeout={BusyTimeoutMs} ms)");
        sb.AppendLine();
        if (_wasMixed)
        {
            sb.AppendLine($"Writes committed: {CommittedWrites}, failed: {FailedWrites}; reads completed: {CompletedReads}.");
            sb.AppendLine();
            LatencyLog.AppendSection(sb, "Write latency (one single-row commit)", _writeLatency);
            LatencyLog.AppendSection(sb, "Read latency (one point read, taken while writers run)", _readLatency);
        }
        else
        {
            sb.AppendLine($"Transactions committed: {CommittedTransactions}, failed: {FailedTransactions}.");
            sb.AppendLine();
            LatencyLog.AppendSection(sb, "Committed transaction latency", _transactionLatency);
            LatencyLog.AppendSection(sb, "Failed transaction latency (time to the exception)", _failedTransactionLatency);
        }

        LatencyLog.WriteSidecar($"{nameof(SqliteFileWriterStormBenchmarks)}-{_arm}-busy{BusyTimeoutMs}-latency.md", sb.ToString());
    }

    private void MarkInvalid(string scenario, string framework, string reason)
    {
        var key = new CorrectnessIssueKey(SQLiteWriteContentionBenchmarks.ParameterKeyFor(BusyTimeoutMs), scenario, framework, reason);
        _issues.AddOrUpdate(key, 1, static (_, count) => count + 1);
    }

    private void Begin(string framework, bool mixed)
    {
        _arm = framework;
        _wasMixed = mixed;
        _committedTransactions = _failedTransactions = _committedWrites = _failedWrites = _completedReads = 0;
        _writersDone = false;
    }

    // ================================================================================================
    // STORM: 100 concurrent writers, each one transaction of 50 updates
    // ================================================================================================

    private async Task<int> TransactionOnAsync(DbConnection connection, int worker)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        for (var j = 0; j < WritesPerTransaction; j++)
        {
            await connection.ExecuteAsync(UpdateSql, new { v = (worker * 1000) + j, id = (j % 100) + 1 }, transaction);
        }

        await transaction.CommitAsync();
        return 0;
    }

    private async Task PengdowsTransactionAsync(DatabaseContext context, int worker, bool applyBusyTimeout)
    {
        await using var transaction = context.BeginTransaction();
        if (applyBusyTimeout)
        {
            await using var setup = transaction.CreateSqlContainer(SqliteFileDatabase.BusyTimeoutSql(BusyTimeoutMs));
            await setup.ExecuteNonQueryAsync();
        }

        for (var j = 0; j < WritesPerTransaction; j++)
        {
            await using var container = transaction.CreateSqlContainer();
            container.Query.Append("UPDATE stress_test SET value = ");
            container.Query.Append(container.MakeParameterName("v"));
            container.Query.Append(" WHERE id = ");
            container.Query.Append(container.MakeParameterName("id"));
            container.AddParameterWithValue("v", DbType.Int32, (worker * 1000) + j);
            container.AddParameterWithValue("id", DbType.Int32, (j % 100) + 1);
            await container.ExecuteNonQueryAsync();
        }

        transaction.Commit();
    }

    private async Task TrackTransactionAsync(string framework, Func<Task> body)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await body();
            stopwatch.Stop();
            _transactionLatency.Add(stopwatch.ElapsedTicks);
            Interlocked.Increment(ref _committedTransactions);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _failedTransactionLatency.Add(stopwatch.ElapsedTicks);
            Interlocked.Increment(ref _failedTransactions);
            MarkInvalid(ScenarioStorm, framework, $"Exception: {ex.GetType().Name}");
        }
    }

    private async Task RunTogetherAsync(int count, Func<int, Task> work)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new Task[count];
        for (var i = 0; i < count; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                await gate.Task;
                await work(index);
            });
        }

        await Task.Delay(50); // let every worker reach the gate before the storm starts
        gate.SetResult();
        await Task.WhenAll(tasks);
    }

    [Benchmark, CorrectnessIdentity(FrameworkDapper, ScenarioStorm)]
    public async Task Storm_Dapper()
    {
        Begin(FrameworkDapper, mixed: false);
        await RunTogetherAsync(StormConcurrency, worker => TrackTransactionAsync(FrameworkDapper, async () =>
        {
            await using var connection = await SqliteFileDatabase.OpenWithSessionAsync(
                _dapperConnectionString, SqliteFileDatabase.BusyTimeoutSql(BusyTimeoutMs));
            await TransactionOnAsync(connection, worker);
        }));
    }

    [Benchmark, CorrectnessIdentity(FrameworkPinned, ScenarioStorm)]
    public async Task Storm_DapperPinnedWriter()
    {
        Begin(FrameworkPinned, mixed: false);
        await RunTogetherAsync(StormConcurrency, worker => TrackTransactionAsync(FrameworkPinned,
            () => _pinned.RunAsync(connection => TransactionOnAsync(connection, worker))));
    }

    [Benchmark, CorrectnessIdentity(FrameworkQueue, ScenarioStorm)]
    public async Task Storm_DapperWriterQueue()
    {
        Begin(FrameworkQueue, mixed: false);
        await RunTogetherAsync(StormConcurrency, worker => TrackTransactionAsync(FrameworkQueue,
            () => _queue.EnqueueAsync(connection => TransactionOnAsync(connection, worker))));
    }

    [Benchmark, CorrectnessIdentity(FrameworkPengdows, ScenarioStorm)]
    public async Task Storm_Pengdows()
    {
        Begin(FrameworkPengdows, mixed: false);
        await RunTogetherAsync(StormConcurrency, worker => TrackTransactionAsync(FrameworkPengdows,
            () => PengdowsTransactionAsync(_pengdows, worker, applyBusyTimeout: true)));
    }

    /// <summary>The same library with none of this benchmark's overrides: it records what a context nobody
    /// configured does under a burst bigger than its default write queue (it rejects, by design).</summary>
    [Benchmark, CorrectnessIdentity(FrameworkPengdowsDefaults, ScenarioStorm)]
    public async Task Storm_PengdowsDefaults()
    {
        Begin(FrameworkPengdowsDefaults, mixed: false);
        await RunTogetherAsync(StormConcurrency, worker => TrackTransactionAsync(FrameworkPengdowsDefaults,
            () => PengdowsTransactionAsync(_pengdowsDefaults, worker, applyBusyTimeout: false)));
    }

    // ================================================================================================
    // MIXED: writers committing single rows while readers read continuously
    // ================================================================================================

    private async Task RunMixedAsync(string framework, Func<int, int, Task> write, Func<int, Task> read)
    {
        Begin(framework, mixed: true);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var writers = new Task[MixedWriters];
        for (var w = 0; w < MixedWriters; w++)
        {
            var writer = w;
            writers[w] = Task.Run(async () =>
            {
                await gate.Task;
                for (var n = 0; n < WritesPerWriter; n++)
                {
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        await write(writer, n);
                        stopwatch.Stop();
                        _writeLatency.Add(stopwatch.ElapsedTicks);
                        Interlocked.Increment(ref _committedWrites);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _failedWrites);
                        MarkInvalid(ScenarioMixed, framework, $"Exception: {ex.GetType().Name}");
                    }
                }
            });
        }

        var readers = new Task[MixedReaders];
        for (var r = 0; r < MixedReaders; r++)
        {
            var reader = r;
            readers[r] = Task.Run(async () =>
            {
                await gate.Task;
                var i = reader;
                // do/while: a reader the thread pool schedules late still reads once, so a short run cannot finish its
                // writes before any reader started and report no reads at all (seen in the unit test's 15-write run).
                do
                {
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        await read(i++);
                        stopwatch.Stop();
                        _readLatency.Add(stopwatch.ElapsedTicks);
                        Interlocked.Increment(ref _completedReads);
                    }
                    catch (Exception ex)
                    {
                        MarkInvalid(ScenarioMixed, framework, $"Read exception: {ex.GetType().Name}");
                    }
                } while (!_writersDone);
            });
        }

        await Task.Delay(50);
        gate.SetResult();
        await Task.WhenAll(writers);
        _writersDone = true;
        await Task.WhenAll(readers);
    }

    private static int RowFor(int i) => (i % SqliteFileDatabase.SeedRows) + 1;

    private async Task PerOperationUpdateAsync(int writer, int n)
    {
        await using var connection = await SqliteFileDatabase.OpenWithSessionAsync(_dapperConnectionString);
        await connection.ExecuteAsync(UpdateSql, new { v = (writer * 1000) + n, id = RowFor((writer * WritesPerWriter) + n) });
    }

    private async Task PerOperationReadAsync(int i)
    {
        await using var connection = await SqliteFileDatabase.OpenWithSessionAsync(_dapperConnectionString);
        await connection.ExecuteScalarAsync<int>(ReadSql, new { id = RowFor(i) });
    }

    [Benchmark, CorrectnessIdentity(FrameworkDapper, ScenarioMixed)]
    public Task Mixed_Dapper() =>
        RunMixedAsync(FrameworkDapper, PerOperationUpdateAsync, PerOperationReadAsync);

    [Benchmark, CorrectnessIdentity(FrameworkPinned, ScenarioMixed)]
    public Task Mixed_DapperPinnedWriter() =>
        RunMixedAsync(FrameworkPinned,
            (writer, n) => _pinned.RunAsync(connection => connection.ExecuteAsync(
                UpdateSql, new { v = (writer * 1000) + n, id = RowFor((writer * WritesPerWriter) + n) })),
            PerOperationReadAsync);

    [Benchmark, CorrectnessIdentity(FrameworkSingle, ScenarioMixed)]
    public Task Mixed_DapperSingleConnection() =>
        RunMixedAsync(FrameworkSingle,
            (writer, n) => _single.RunAsync(connection => connection.ExecuteAsync(
                UpdateSql, new { v = (writer * 1000) + n, id = RowFor((writer * WritesPerWriter) + n) })),
            i => _single.RunAsync(connection => connection.ExecuteScalarAsync<int>(ReadSql, new { id = RowFor(i) })));

    [Benchmark, CorrectnessIdentity(FrameworkQueue, ScenarioMixed)]
    public Task Mixed_DapperWriterQueue() =>
        RunMixedAsync(FrameworkQueue,
            (writer, n) => _queue.EnqueueAsync(connection => connection.ExecuteAsync(
                UpdateSql, new { v = (writer * 1000) + n, id = RowFor((writer * WritesPerWriter) + n) })),
            PerOperationReadAsync);

    [Benchmark, CorrectnessIdentity(FrameworkPengdows, ScenarioMixed)]
    public Task Mixed_Pengdows() => MixedPengdowsAsync(_pengdows, FrameworkPengdows);

    [Benchmark, CorrectnessIdentity(FrameworkPengdowsFairnessOff, ScenarioMixed)]
    public Task Mixed_Pengdows_FairnessOff() => MixedPengdowsAsync(_pengdowsFairnessOff, FrameworkPengdowsFairnessOff);

    private Task MixedPengdowsAsync(DatabaseContext context, string framework) =>
        RunMixedAsync(framework,
            async (writer, n) =>
            {
                await using var container = context.CreateSqlContainer();
                container.Query.Append("UPDATE stress_test SET value = ");
                container.Query.Append(container.MakeParameterName("v"));
                container.Query.Append(" WHERE id = ");
                container.Query.Append(container.MakeParameterName("id"));
                container.AddParameterWithValue("v", DbType.Int32, (writer * 1000) + n);
                container.AddParameterWithValue("id", DbType.Int32, RowFor((writer * WritesPerWriter) + n));
                await container.ExecuteNonQueryAsync();
            },
            async i =>
            {
                await using var container = context.CreateSqlContainer();
                container.Query.Append("SELECT value FROM stress_test WHERE id = ");
                container.Query.Append(container.MakeParameterName("id"));
                container.AddParameterWithValue("id", DbType.Int32, RowFor(i));
                await container.ExecuteScalarRequiredAsync<int>();
            });
}
