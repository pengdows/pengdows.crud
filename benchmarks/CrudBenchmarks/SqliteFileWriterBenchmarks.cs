using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// What one write costs against a real file in WAL mode, for each way of serializing SQLite writes found in real code
/// and libraries. This is the COST question, run sequentially so nothing fails: read it next to
/// SqliteFileWriterStormBenchmarks (what happens when writers collide) and SqliteFileWriterPoisonBenchmarks (what
/// happens when the writer's connection goes bad). A loss here is a price; the other two classes show what it buys.
///
/// Arms, per operation:
///   _Dapper                    a new connection per operation, no write coordination (SQLAlchemy 1.4's shape).
///   _DapperPinnedWriter        one long-lived writer connection behind a gate (sqlite-net-pcl, GRDB, a size-1 pool).
///   _DapperSingleConnection    that same connection and gate for reads too (reads are serialized behind writes).
///   _DapperWriterQueue         a channel drained by one background writer (EntityFrameworkCore.Sqlite.Concurrency).
///   _Pengdows                  DbMode.Best, which is SingleWriter for a SQLite file: governor, ephemeral connections.
///
/// Every arm runs the same session settings (foreign keys on, query_only off, synchronous NORMAL; see
/// SqliteFileDatabase.SessionSql): pengdows.crud on every checkout, the per-operation Dapper arms on every open, the
/// pinned, queue and single-connection arms once. All arms run in WAL mode. Each write is its own commit.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SqliteFileWriterBenchmarks : IDisposable
{
    private const string UpdateSql = "UPDATE stress_test SET value = @v WHERE id = @id";
    private const string ReadSql = "SELECT value FROM stress_test WHERE id = @id";

    private SqliteFileDatabase _database = null!;
    private string _dapperConnectionString = null!;
    private PinnedWriterArm _pinned = null!;
    private PinnedWriterArm _single = null!;
    private WriterQueueArm _queue = null!;
    private DatabaseContext _pengdows = null!;
    private ISqlContainer _updateSc = null!;
    private ISqlContainer _readSc = null!;

    [Params(1, 100)] public int RecordCount { get; set; }

    internal string DatabaseConnectionStringForTests => _dapperConnectionString;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _database = new SqliteFileDatabase();
        _dapperConnectionString = _database.DapperConnectionString();
        _pinned = new PinnedWriterArm(_dapperConnectionString, SqliteFileDatabase.SessionSql);
        _single = new PinnedWriterArm(_dapperConnectionString, SqliteFileDatabase.SessionSql);
        _queue = new WriterQueueArm(_dapperConnectionString, SqliteFileDatabase.SessionSql);

        _pengdows = new DatabaseContext(_database.PengdowsConnectionString(), SqliteFactory.Instance);
        if (_pengdows.ConnectionMode != DbMode.SingleWriter)
        {
            throw new InvalidOperationException(
                $"The pengdows arm must run in SingleWriter, the mode a default context gets for a SQLite file; it is in {_pengdows.ConnectionMode}.");
        }

        _updateSc = _pengdows.CreateSqlContainer();
        _updateSc.Query.Append("UPDATE stress_test SET value = ");
        _updateSc.Query.Append(_updateSc.MakeParameterName("v"));
        _updateSc.Query.Append(" WHERE id = ");
        _updateSc.Query.Append(_updateSc.MakeParameterName("id"));
        _updateSc.AddParameterWithValue("v", DbType.Int32, 0);
        _updateSc.AddParameterWithValue("id", DbType.Int32, 1);

        _readSc = _pengdows.CreateSqlContainer();
        _readSc.Query.Append("SELECT value FROM stress_test WHERE id = ");
        _readSc.Query.Append(_readSc.MakeParameterName("id"));
        _readSc.AddParameterWithValue("id", DbType.Int32, 1);

        await Task.CompletedTask;
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _updateSc?.Dispose();
        _readSc?.Dispose();
        _pengdows?.Dispose();
        _queue?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _single?.Dispose();
        _pinned?.Dispose();
        _database?.Dispose();
    }

    public void Dispose() => GlobalCleanup();

    private static int RowFor(int i) => (i % SqliteFileDatabase.SeedRows) + 1;

    // ---- writes ---------------------------------------------------------------------------------------------

    [Benchmark(Baseline = true), BenchmarkCategory("Write")]
    public async Task<int> Write_Dapper()
    {
        var affected = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var connection = await SqliteFileDatabase.OpenWithSessionAsync(_dapperConnectionString);
            affected += await connection.ExecuteAsync(UpdateSql, new { v = i, id = RowFor(i) });
        }

        return affected;
    }

    [Benchmark, BenchmarkCategory("Write")]
    public async Task<int> Write_DapperPinnedWriter()
    {
        var affected = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await _pinned.Gate.WaitAsync();
            try
            {
                affected += await _pinned.Connection.ExecuteAsync(UpdateSql, new { v = i, id = RowFor(i) });
            }
            finally
            {
                _pinned.Gate.Release();
            }
        }

        return affected;
    }

    [Benchmark, BenchmarkCategory("Write")]
    public async Task<int> Write_DapperWriterQueue()
    {
        var affected = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var value = i;
            var id = RowFor(i);
            affected += await _queue.EnqueueAsync(connection =>
                connection.ExecuteAsync(UpdateSql, new { v = value, id }));
        }

        return affected;
    }

    [Benchmark, BenchmarkCategory("Write")]
    public async Task<int> Write_Pengdows()
    {
        var affected = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            _updateSc.SetParameterValue("v", i);
            _updateSc.SetParameterValue("id", RowFor(i));
            affected += await _updateSc.ExecuteNonQueryAsync();
        }

        return affected;
    }

    // ---- reads ----------------------------------------------------------------------------------------------

    [Benchmark(Baseline = true), BenchmarkCategory("Read")]
    public async Task<int> ReadSingle_Dapper()
    {
        var value = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var connection = await SqliteFileDatabase.OpenWithSessionAsync(_dapperConnectionString);
            value = await connection.ExecuteScalarAsync<int>(ReadSql, new { id = RowFor(i) });
        }

        return value;
    }

    [Benchmark, BenchmarkCategory("Read")]
    public async Task<int> ReadSingle_DapperSingleConnection()
    {
        var value = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await _single.Gate.WaitAsync();
            try
            {
                value = await _single.Connection.ExecuteScalarAsync<int>(ReadSql, new { id = RowFor(i) });
            }
            finally
            {
                _single.Gate.Release();
            }
        }

        return value;
    }

    [Benchmark, BenchmarkCategory("Read")]
    public async Task<int> ReadSingle_Pengdows()
    {
        var value = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            _readSc.SetParameterValue("id", RowFor(i));
            value = await _readSc.ExecuteScalarRequiredAsync<int>();
        }

        return value;
    }
}
