using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using DuckDB.NET.Data;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

/// <summary>
/// Serial write overhead on one DuckDB file: one thread, no contention, 1,000 read-modify-write transactions per call.
/// Separates the per-transaction cost of each path from the cost of serializing concurrent writers (see
/// DuckDbWriterStormBenchmarks for the concurrent case).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class DuckDbWriteOverheadBenchmarks : IDisposable
{
    private const int Transactions = 1000;
    private const int HotRows = 10;

    private string _dbFile = null!;
    private DatabaseContext _singleWriter = null!;
    private DatabaseContext _standard = null!;
    private PrebuiltContainerPool _reads = null!;
    private PrebuiltContainerPool _writes = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"pengdows_duckdb_overhead_{Guid.NewGuid():N}.duckdb");
        using (var seed = new DuckDBConnection($"Data Source={_dbFile}"))
        {
            seed.Open();
            seed.Execute("CREATE TABLE hot (id INTEGER PRIMARY KEY, value INTEGER)");
            seed.Execute($"INSERT INTO hot SELECT range AS id, 0 AS value FROM range(1, {HotRows + 1})");
        }

        _singleWriter = NewContext(DbMode.SingleWriter);
        _standard = NewContext(DbMode.Standard);
        _reads = new PrebuiltContainerPool(() => BuildRead(_singleWriter));
        _writes = new PrebuiltContainerPool(() => BuildWrite(_singleWriter));
    }

    private DatabaseContext NewContext(DbMode mode) => new(new DatabaseContextConfiguration
    {
        ConnectionString = $"Data Source={_dbFile}",
        ReadWriteMode = ReadWriteMode.ReadWrite,
        DbMode = mode,
    }, DuckDBClientFactory.Instance, null, new TypeMapRegistry());

    private static ISqlContainer BuildRead(DatabaseContext context)
    {
        var container = context.CreateSqlContainer();
        container.Query.Append("SELECT value FROM hot WHERE id = ");
        container.Query.Append(container.MakeParameterName("id"));
        container.AddParameterWithValue("id", DbType.Int32, 1);
        return container;
    }

    private static ISqlContainer BuildWrite(DatabaseContext context)
    {
        var container = context.CreateSqlContainer();
        container.Query.Append("UPDATE hot SET value = ");
        container.Query.Append(container.MakeParameterName("value"));
        container.Query.Append(" WHERE id = ");
        container.Query.Append(container.MakeParameterName("id"));
        container.AddParameterWithValue("value", DbType.Int32, 0);
        container.AddParameterWithValue("id", DbType.Int32, 1);
        return container;
    }

    [Benchmark(Baseline = true)]
    public async Task Dapper_SerialTransactions()
    {
        for (var i = 0; i < Transactions; i++)
        {
            var id = (i % HotRows) + 1;
            await using var connection = new DuckDBConnection($"Data Source={_dbFile}");
            await connection.OpenAsync();
            await using var tx = await connection.BeginTransactionAsync();
            var value = await connection.ExecuteScalarAsync<int>("SELECT value FROM hot WHERE id = $id", new { id }, tx);
            await connection.ExecuteAsync("UPDATE hot SET value = $value WHERE id = $id", new { value = value + 1, id }, tx);
            await tx.CommitAsync();
        }
    }

    [Benchmark]
    public async Task Pengdows_Standard_SerialTransactions()
    {
        for (var i = 0; i < Transactions; i++)
        {
            await using var tx = await _standard.BeginTransactionAsync();
            await using var read = _standard.CreateSqlContainer();
            read.Query.Append("SELECT value FROM hot WHERE id = ");
            read.Query.Append(read.MakeParameterName("id"));
            read.AddParameterWithValue("id", DbType.Int32, (i % HotRows) + 1);
            var value = await read.Clone(tx).ExecuteScalarRequiredAsync<int>();
            await using var write = _standard.CreateSqlContainer();
            write.Query.Append("UPDATE hot SET value = ");
            write.Query.Append(write.MakeParameterName("value"));
            write.Query.Append(" WHERE id = ");
            write.Query.Append(write.MakeParameterName("id"));
            write.AddParameterWithValue("value", DbType.Int32, value + 1);
            write.AddParameterWithValue("id", DbType.Int32, (i % HotRows) + 1);
            await write.Clone(tx).ExecuteNonQueryAsync();
            tx.Commit();
        }
    }

    [Benchmark]
    public async Task Pengdows_SingleWriter_SerialTransactions()
    {
        for (var i = 0; i < Transactions; i++)
        {
            await using var tx = await _singleWriter.BeginTransactionAsync();
            await using var readLease = _reads.Lease();
            await using var read = readLease.Container.Clone(tx);
            read.SetParameterValue("id", (i % HotRows) + 1);
            var value = await read.ExecuteScalarRequiredAsync<int>();
            await using var writeLease = _writes.Lease();
            await using var write = writeLease.Container.Clone(tx);
            write.SetParameterValue("value", value + 1);
            write.SetParameterValue("id", (i % HotRows) + 1);
            await write.ExecuteNonQueryAsync();
            tx.Commit();
        }
    }

    [Benchmark]
    public async Task Pengdows_SingleWriter_AutoCommitWrites()
    {
        await using var writeLease = _writes.Lease();
        for (var i = 0; i < Transactions; i++)
        {
            writeLease.Container.SetParameterValue("value", i);
            writeLease.Container.SetParameterValue("id", (i % HotRows) + 1);
            await writeLease.Container.ExecuteNonQueryAsync();
        }
    }

    // Diagnostics: where the per-commit cost sits. One persistent connection, so no open or close per transaction.
    [Benchmark]
    public async Task Dapper_SerialTransactions_InMemory_OneConnection()
    {
        await using var connection = new DuckDBConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await connection.ExecuteAsync("CREATE TABLE hot (id INTEGER PRIMARY KEY, value INTEGER)");
        await connection.ExecuteAsync($"INSERT INTO hot SELECT range AS id, 0 AS value FROM range(1, {HotRows + 1})");
        for (var i = 0; i < Transactions; i++)
        {
            var id = (i % HotRows) + 1;
            await using var tx = await connection.BeginTransactionAsync();
            var value = await connection.ExecuteScalarAsync<int>("SELECT value FROM hot WHERE id = $id", new { id }, tx);
            await connection.ExecuteAsync("UPDATE hot SET value = $value WHERE id = $id", new { value = value + 1, id }, tx);
            await tx.CommitAsync();
        }
    }

    [Benchmark]
    public async Task Dapper_SerialTransactions_File_OneConnection()
    {
        await using var connection = new DuckDBConnection($"Data Source={_dbFile}");
        await connection.OpenAsync();
        for (var i = 0; i < Transactions; i++)
        {
            var id = (i % HotRows) + 1;
            await using var tx = await connection.BeginTransactionAsync();
            var value = await connection.ExecuteScalarAsync<int>("SELECT value FROM hot WHERE id = $id", new { id }, tx);
            await connection.ExecuteAsync("UPDATE hot SET value = $value WHERE id = $id", new { value = value + 1, id }, tx);
            await tx.CommitAsync();
        }
    }

    // Diagnostics: the cost of opening and closing a DuckDB file connection, with no statements at all.
    [Benchmark]
    public async Task Open_Close_Only_File()
    {
        for (var i = 0; i < Transactions; i++)
        {
            await using var connection = new DuckDBConnection($"Data Source={_dbFile}");
            await connection.OpenAsync();
        }
    }

    public void Dispose()
    {
        _reads?.Dispose();
        _writes?.Dispose();
        _singleWriter?.Dispose();
        _standard?.Dispose();
        try
        {
            File.Delete(_dbFile);
        }
        catch (IOException)
        {
            // The file may still be held by a closing connection; it lives in the temp directory either way.
        }
    }
}
