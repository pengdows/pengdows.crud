using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Dapper;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace CrudBenchmarks;

/// <summary>
/// A poisoned pooled connection on a real SQLite file. Microsoft.Data.Sqlite pools physical connections per connection
/// string and does not reset their pragmas. A rogue component turns foreign_keys OFF on a pooled connection and returns
/// it. Each arm then inserts child rows that reference a parent which does not exist.
/// <list type="bullet">
/// <item>Dapper, connection per operation: draws the poisoned connection from the pool. The orphan rows go in silently.</item>
/// <item>pengdows, same connection string and pool: re-asserts foreign_keys on every checkout, so the orphan inserts are
/// rejected with a ForeignKeyViolationException.</item>
/// </list>
/// The measure is the count of orphan rows left in the table, not the time. Read the Orphans column.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 0, iterationCount: 1, invocationCount: 1)]
public class SqlitePoisonedPoolBenchmarks : IDisposable
{
    private const int Attempts = 100;

    private string _path = null!;
    private string _connectionString = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _path = Path.Combine(Path.GetTempPath(), $"pengdows_sqlite_poison_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_path}";
    }

    // A fresh schema and an empty pool.
    private void ArrangeSchema()
    {
        SqliteConnection.ClearAllPools();
        using (var setup = new SqliteConnection(_connectionString))
        {
            setup.Open();
            setup.Execute("DROP TABLE IF EXISTS child");
            setup.Execute("DROP TABLE IF EXISTS parent");
            setup.Execute("CREATE TABLE parent (id INTEGER PRIMARY KEY)");
            setup.Execute("CREATE TABLE child (id INTEGER PRIMARY KEY, parent_id INTEGER REFERENCES parent(id))");
        }
    }

    // The rogue: turns enforcement off on a pooled connection, then returns it to the pool. Called after a context exists,
    // so the context has already captured its clean defaults; what it does later is the question.
    private void Poison()
    {
        using var rogue = new SqliteConnection(_connectionString);
        rogue.Open();
        rogue.Execute("PRAGMA foreign_keys = OFF");
    }

    private int CountOrphans()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM child WHERE parent_id NOT IN (SELECT id FROM parent)");
    }

    [Benchmark(Baseline = true)]
    public async Task Dapper_ConnectionPerOperation()
    {
        ArrangeSchema();
        Poison();
        var inserted = 0;
        var rejected = 0;
        for (var i = 0; i < Attempts; i++)
        {
            try
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                await connection.ExecuteAsync(
                    "INSERT INTO child (id, parent_id) VALUES (@id, @parent)", new { id = i + 1, parent = 999 });
                inserted++;
            }
            catch (SqliteException)
            {
                rejected++;
            }
        }

        Console.WriteLine($"[SqlitePoisonedPool] Dapper_ConnectionPerOperation: inserted={inserted} rejected={rejected} orphans={CountOrphans()} of {Attempts}");
    }

    [Benchmark]
    public async Task Pengdows_Normal_SharedPool()
    {
        ArrangeSchema();
        using var context = new DatabaseContext(_connectionString, SqliteFactory.Instance);
        Poison();
        var inserted = 0;
        var rejected = 0;
        for (var i = 0; i < Attempts; i++)
        {
            try
            {
                await using var container = context.CreateSqlContainer(
                    "INSERT INTO child (id, parent_id) VALUES (@id, @parent)");
                container.AddParameterWithValue("id", System.Data.DbType.Int32, i + 1);
                container.AddParameterWithValue("parent", System.Data.DbType.Int32, 999);
                await container.ExecuteNonQueryAsync();
                inserted++;
            }
            catch (Exception)
            {
                rejected++;
            }
        }

        Console.WriteLine($"[SqlitePoisonedPool] Pengdows_Normal_SharedPool: inserted={inserted} rejected={rejected} orphans={CountOrphans()} of {Attempts}");
    }

    [Benchmark]
    public async Task Pengdows_Paranoid_SharedPool()
    {
        ArrangeSchema();
        using var context = new DatabaseContext(new DatabaseContextConfiguration { ConnectionString = _connectionString, SessionSettingsMode = SessionSettingsMode.Paranoid }, SqliteFactory.Instance);
        Poison();
        var inserted = 0;
        var rejected = 0;
        for (var i = 0; i < Attempts; i++)
        {
            try
            {
                await using var container = context.CreateSqlContainer(
                    "INSERT INTO child (id, parent_id) VALUES (@id, @parent)");
                container.AddParameterWithValue("id", System.Data.DbType.Int32, i + 1);
                container.AddParameterWithValue("parent", System.Data.DbType.Int32, 999);
                await container.ExecuteNonQueryAsync();
                inserted++;
            }
            catch (Exception)
            {
                rejected++;
            }
        }

        Console.WriteLine($"[SqlitePoisonedPool] Pengdows_Paranoid_SharedPool: inserted={inserted} rejected={rejected} orphans={CountOrphans()} of {Attempts}");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // The file may still be held by a closing connection; it lives in the temp directory either way.
        }
    }

    public void Dispose() => GlobalCleanup();
}
