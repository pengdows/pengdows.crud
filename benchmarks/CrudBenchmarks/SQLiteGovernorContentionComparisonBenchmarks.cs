using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3, invocationCount: 1)]
public class SQLiteGovernorContentionComparisonBenchmarks : IAsyncDisposable
{
    [Params(1)]
    public int MaxSlots { get; set; }

    [Params(32, 100)]
    public int Workers { get; set; }

    [Params(10)]
    public int WritesPerWorker { get; set; }

    private string _connectionString = null!;
    private SqliteConnection _sentinel = null!;
    private PoolGovernor _semaphoreGovernor = null!;
    private SemaphoreSlim _semaphoreAdmission = null!;
    private PoolGovernorAdaptiveCopy _adaptiveGovernor = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _connectionString = $"Data Source=governor_compare_{Guid.NewGuid():N}.db;Mode=Memory;Cache=Shared;Default Timeout=30";
        _sentinel = new SqliteConnection(_connectionString);
        await _sentinel.OpenAsync();

        await using (var command = _sentinel.CreateCommand())
        {
            command.CommandText = "CREATE TABLE stress_test (id INTEGER PRIMARY KEY, value INTEGER NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = _sentinel.CreateCommand())
        {
            command.CommandText = "INSERT INTO stress_test (id, value) VALUES (1, 0);";
            await command.ExecuteNonQueryAsync();
        }

        var queueDepth = Workers * 2;
        _semaphoreAdmission = new SemaphoreSlim(MaxSlots, MaxSlots);
        _semaphoreGovernor = new PoolGovernor(
            PoolLabel.Writer, "sqlite-compare", MaxSlots, TimeSpan.FromSeconds(30), maxQueueDepth: queueDepth,
            sharedSemaphore: _semaphoreAdmission);
        _adaptiveGovernor = new PoolGovernorAdaptiveCopy(
            PoolLabel.Writer, "sqlite-compare", MaxSlots, TimeSpan.FromSeconds(30), maxQueueDepth: queueDepth);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _semaphoreGovernor.Dispose();
        _semaphoreAdmission.Dispose();
        _adaptiveGovernor.Dispose();
        await _sentinel.DisposeAsync();
    }

    [Benchmark(Baseline = true)]
    public Task SemaphoreGovernorSQLiteContention() => RunAsync(_semaphoreGovernor);

    [Benchmark]
    public Task AdaptiveGovernorSQLiteContention() => RunAsync(_adaptiveGovernor);

    private async Task RunAsync(PoolGovernor governor)
    {
        var workers = Enumerable.Range(0, Workers)
            .Select(_ => RunWorkerAsync(async () =>
            {
                await using var slot = await governor.AcquireAsync().ConfigureAwait(false);
                await ExecuteWritesAsync().ConfigureAwait(false);
            }))
            .ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task RunAsync(PoolGovernorAdaptiveCopy governor)
    {
        var workers = Enumerable.Range(0, Workers)
            .Select(_ => RunWorkerAsync(async () =>
            {
                await using var slot = await governor.AcquireAsync().ConfigureAwait(false);
                await ExecuteWritesAsync().ConfigureAwait(false);
            }))
            .ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private static async Task RunWorkerAsync(Func<Task> work) => await work().ConfigureAwait(false);

    private async Task ExecuteWritesAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "UPDATE stress_test SET value = $value WHERE id = 1;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$value";
        command.Parameters.Add(parameter);

        for (var i = 0; i < WritesPerWorker; i++)
        {
            parameter.Value = i;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _semaphoreGovernor?.Dispose();
        _adaptiveGovernor?.Dispose();
        return _sentinel == null ? ValueTask.CompletedTask : _sentinel.DisposeAsync();
    }
}
