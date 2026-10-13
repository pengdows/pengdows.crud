using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Dapper;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace CrudBenchmarks;

/// <summary>
/// FAULT INJECTION, NOT A SPEED CLAIM, AND NOT A TIMING: read the Fails column. The same run of transactions through
/// each way of serializing SQLite writes, with the writer's connection dying in the middle of one of them (fakeDb's
/// <c>BreakConnection()</c> marks the connection Broken; commands, transactions and reopening on it throw). The
/// question is what the death costs, counted in failed transactions:
///
///   Poison_DapperPinnedWriter                the plain pinned writer has nothing that notices, so it fails the
///                                            transaction in flight and every one after it.
///   Poison_DapperPinnedWriterWithReconnect   with reconnect logic it loses the transaction in flight.
///   Poison_Pengdows                          pengdows.crud loses the transaction in flight too, because it keeps no
///                                            connection between operations: the next one checks out a new one.
///
/// So the reconnect arm and pengdows end level on failures. What they do not share is the code: the reconnect arm is
/// the extra logic a hand-rolled pinned writer has to carry. The database behind every arm is fakeDb, which is why
/// the times mean nothing here; fakeDb is also what the library's own tests use for this (BrokenConnectionRecoveryTests).
///
/// What a sever is not: SQLite is a file, so a connection dies far less often than one to a server. The fault is
/// injected to show the shape of the failure, not to claim it is common on SQLite.
/// </summary>
[MemoryDiagnoser]
// One invocation per run: the Fails column adds up across every invocation, so a method BenchmarkDotNet calls
// thousands of times would report thousands of failures. One run is the count that means something.
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 0, iterationCount: 1, invocationCount: 1)]
public class SqliteFileWriterPoisonBenchmarks : IDisposable
{
    private const string ScenarioPoison = "Poison";
    private const string FrameworkPinned = "DapperPinnedWriter";
    private const string FrameworkReconnect = "DapperPinnedWriterWithReconnect";
    private const string FrameworkPengdows = "Pengdows";
    private const string UpdateSql = "UPDATE stress_test SET value = @v WHERE id = @id";

    public int TransactionCount { get; set; } = 200;
    public int SeverAtTransaction { get; set; } = 100;

    private readonly ConcurrentDictionary<CorrectnessIssueKey, int> _issues = new();
    private int _committed;
    private int _failed;

    internal int CommittedTransactions => Volatile.Read(ref _committed);
    internal int FailedTransactions => Volatile.Read(ref _failed);

    [GlobalSetup]
    public Task GlobalSetup() => Task.CompletedTask;

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        BenchmarkCorrectnessArtifacts.Write(nameof(SqliteFileWriterPoisonBenchmarks),
            _issues
                .OrderBy(p => p.Key.Scenario, StringComparer.Ordinal)
                .ThenBy(p => p.Key.Framework, StringComparer.Ordinal)
                .ThenBy(p => p.Key.Reason, StringComparer.Ordinal)
                .Select(p => new CorrectnessIssue(null, p.Key.Scenario, p.Key.Framework, p.Key.Reason, p.Value))
                .ToArray());
    }

    public void Dispose() => GlobalCleanup();

    // BenchmarkDotNet runs one extra invocation to JIT the method even with no warmup, so the issues are cleared at the
    // start of every run and the artifact written at cleanup holds the last run only: its Fails column is then the
    // failed transactions of ONE run (out of TransactionCount), not the sum of the JIT run and the measured one.
    private void Reset()
    {
        _committed = 0;
        _failed = 0;
        _issues.Clear();
    }

    private void Committed() => Interlocked.Increment(ref _committed);

    private void Failed(string framework, Exception ex)
    {
        Interlocked.Increment(ref _failed);
        var key = new CorrectnessIssueKey("*", ScenarioPoison, framework, $"Exception: {ex.GetType().Name}");
        _issues.AddOrUpdate(key, 1, static (_, count) => count + 1);
    }

    private static void SeverEveryConnection(fakeDbFactory factory)
    {
        foreach (var connection in factory.CreatedConnections.ToArray())
        {
            connection.BreakConnection();
        }
    }

    private static DbConnection NewConnection(fakeDbFactory factory)
    {
        var connection = factory.CreateConnection();
        connection.ConnectionString = "Data Source=poison.db";
        return connection;
    }

    /// <summary>Two statements in one transaction; on the severing transaction the connection dies between them.</summary>
    private async Task<int> PinnedTransactionAsync(fakeDbFactory factory, DbConnection connection, int index)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(UpdateSql, new { v = index, id = 1 }, transaction);
        if (index == SeverAtTransaction)
        {
            SeverEveryConnection(factory);
        }

        await connection.ExecuteAsync(UpdateSql, new { v = index, id = 2 }, transaction);
        await transaction.CommitAsync();
        return 0;
    }

    [Benchmark, CorrectnessIdentity(FrameworkPinned, ScenarioPoison)]
    public async Task Poison_DapperPinnedWriter()
    {
        Reset();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var writer = new PinnedWriterArm(() => NewConnection(factory));
        for (var index = 0; index < TransactionCount; index++)
        {
            try
            {
                await writer.RunAsync(connection => PinnedTransactionAsync(factory, connection, index));
                Committed();
            }
            catch (Exception ex)
            {
                Failed(FrameworkPinned, ex);
            }
        }
    }

    [Benchmark, CorrectnessIdentity(FrameworkReconnect, ScenarioPoison)]
    public async Task Poison_DapperPinnedWriterWithReconnect()
    {
        Reset();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var writer = new PinnedWriterArm(() => NewConnection(factory));
        for (var index = 0; index < TransactionCount; index++)
        {
            try
            {
                await writer.RunAsync(connection => PinnedTransactionAsync(factory, connection, index));
                Committed();
            }
            catch (Exception ex)
            {
                Failed(FrameworkReconnect, ex);
                writer.Reconnect();
            }
        }
    }

    [Benchmark, CorrectnessIdentity(FrameworkPengdows, ScenarioPoison)]
    public async Task Poison_Pengdows()
    {
        Reset();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=poison.db", factory);
        for (var index = 0; index < TransactionCount; index++)
        {
            try
            {
                await using var transaction = context.BeginTransaction();
                await UpdateAsync(transaction, index, 1);
                if (index == SeverAtTransaction)
                {
                    SeverEveryConnection(factory);
                }

                await UpdateAsync(transaction, index, 2);
                transaction.Commit();
                Committed();
            }
            catch (Exception ex)
            {
                Failed(FrameworkPengdows, ex);
            }
        }
    }

    private static async Task UpdateAsync(IDatabaseContext context, int value, int id)
    {
        await using var container = context.CreateSqlContainer();
        container.Query.Append("UPDATE stress_test SET value = ");
        container.Query.Append(container.MakeParameterName("v"));
        container.Query.Append(" WHERE id = ");
        container.Query.Append(container.MakeParameterName("id"));
        container.AddParameterWithValue("v", DbType.Int32, value);
        container.AddParameterWithValue("id", DbType.Int32, id);
        await container.ExecuteNonQueryAsync();
    }
}
