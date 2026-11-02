using System.Data;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Dapper;
using DuckDB.NET.Data;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

/// <summary>
/// Concurrent read-modify-write on one DuckDB file. Writers contend on a few hot rows inside explicit transactions.
/// The naive arm is Dapper with a connection per operation and no write coordination (what most code does). The
/// pengdows arm runs the same transactions on a DbMode.SingleWriter context. Each arm reports its conflicts (failed
/// transactions) and checks for lost updates: every committed increment must appear in the stored values.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class DuckDbWriterStormBenchmarks : IDisposable
{
    private const int Writers = 50;
    private const int WritesPerWriter = 20;
    private const int HotRows = 10;

    private string _dbFile = null!;
    private DatabaseContext _context = null!;
    private PrebuiltContainerPool _reads = null!;
    private PrebuiltContainerPool _writes = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"pengdows_duckdb_storm_{Guid.NewGuid():N}.duckdb");
        using (var seed = new DuckDBConnection($"Data Source={_dbFile}"))
        {
            seed.Open();
            seed.Execute("CREATE TABLE hot (id INTEGER PRIMARY KEY, value INTEGER)");
            seed.Execute($"INSERT INTO hot SELECT range AS id, 0 AS value FROM range(1, {HotRows + 1})");
        }

        _context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={_dbFile}",
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.SingleWriter,
        }, DuckDBClientFactory.Instance, null, new TypeMapRegistry());

        // Each statement is built once per pool slot; a transaction clones it and sets the values.
        _reads = new PrebuiltContainerPool(BuildRead);
        _writes = new PrebuiltContainerPool(BuildWrite);
    }

    private ISqlContainer BuildRead()
    {
        var container = _context.CreateSqlContainer();
        container.Query.Append("SELECT value FROM hot WHERE id = ");
        container.Query.Append(container.MakeParameterName("id"));
        container.AddParameterWithValue("id", DbType.Int32, 1);
        return container;
    }

    private ISqlContainer BuildWrite()
    {
        var container = _context.CreateSqlContainer();
        container.Query.Append("UPDATE hot SET value = ");
        container.Query.Append(container.MakeParameterName("value"));
        container.Query.Append(" WHERE id = ");
        container.Query.Append(container.MakeParameterName("id"));
        container.AddParameterWithValue("value", DbType.Int32, 0);
        container.AddParameterWithValue("id", DbType.Int32, 1);
        return container;
    }

    [Benchmark(Baseline = true)]
    public async Task Dapper_ConnectionPerOperation()
    {
        await ResetAsync();
        var failures = 0;
        var committed = 0;
        await RunWritersAsync(async (writer, n) =>
        {
            var id = HotRow(writer, n);
            await using var connection = new DuckDBConnection($"Data Source={_dbFile}");
            await connection.OpenAsync();
            await using var tx = await connection.BeginTransactionAsync();
            var value = await connection.ExecuteScalarAsync<int>(
                "SELECT value FROM hot WHERE id = $id", new { id }, tx);
            await connection.ExecuteAsync(
                "UPDATE hot SET value = $value WHERE id = $id", new { value = value + 1, id }, tx);
            await tx.CommitAsync();
            Interlocked.Increment(ref committed);
        }, () => Interlocked.Increment(ref failures));
        await ReportAsync("Dapper_ConnectionPerOperation", failures, committed);
    }

    [Benchmark]
    public async Task Pengdows_SingleWriter()
    {
        await ResetAsync();
        var failures = 0;
        var committed = 0;
        await RunWritersAsync(async (writer, n) =>
        {
            var id = HotRow(writer, n);
            await using var tx = await _context.BeginTransactionAsync();
            await using var readLease = _reads.Lease();
            await using var read = readLease.Container.Clone(tx);
            read.SetParameterValue("id", id);
            var value = await read.ExecuteScalarRequiredAsync<int>();
            await using var writeLease = _writes.Lease();
            await using var write = writeLease.Container.Clone(tx);
            write.SetParameterValue("value", value + 1);
            write.SetParameterValue("id", id);
            await write.ExecuteNonQueryAsync();
            tx.Commit();
            Interlocked.Increment(ref committed);
        }, () => Interlocked.Increment(ref failures));
        await ReportAsync("Pengdows_SingleWriter", failures, committed);
    }

    // Writers share a few hot rows, so two transactions read and update the same row at about the same time.
    private static int HotRow(int writer, int n) => ((writer * WritesPerWriter) + n) % HotRows + 1;

    private static async Task RunWritersAsync(Func<int, int, Task> write, Action onFailure)
    {
        var tasks = new Task[Writers];
        for (var w = 0; w < Writers; w++)
        {
            var writer = w;
            tasks[w] = Task.Run(async () =>
            {
                for (var n = 0; n < WritesPerWriter; n++)
                {
                    try
                    {
                        await write(writer, n);
                    }
                    catch (Exception)
                    {
                        onFailure();
                    }
                }
            });
        }

        await Task.WhenAll(tasks);
    }

    private async Task ResetAsync()
    {
        await using var connection = new DuckDBConnection($"Data Source={_dbFile}");
        await connection.OpenAsync();
        await connection.ExecuteAsync("UPDATE hot SET value = 0");
    }

    // Every committed increment must be in the stored values; a mismatch is a lost update.
    private async Task ReportAsync(string arm, int failures, int committed)
    {
        await using var connection = new DuckDBConnection($"Data Source={_dbFile}");
        await connection.OpenAsync();
        var stored = await connection.ExecuteScalarAsync<long>("SELECT CAST(COALESCE(SUM(value), 0) AS BIGINT) FROM hot");
        Console.WriteLine(
            $"[DuckDbWriterStorm] {arm}: committed={committed} failed={failures} stored={stored} lost={committed - stored} of {Writers * WritesPerWriter}");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _reads?.Dispose();
        _writes?.Dispose();
        _context?.Dispose();
        try
        {
            File.Delete(_dbFile);
        }
        catch (IOException)
        {
            // The file may still be held by a closing connection; it lives in the temp directory either way.
        }
    }

    public void Dispose() => GlobalCleanup();
}
