using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// synchronous = NORMAL: in WAL mode it keeps every committed transaction through an application crash and only puts the
// newest commits at risk in a power failure (the database is never corrupted); FULL syncs the log on every commit.
// It is only that safe in WAL. In a rollback journal NORMAL can corrupt on power loss, so it is applied only when the
// database really is in WAL (a context that cannot get WAL, such as a memory database, keeps SQLite's own default).
// It is per-connection state, so it is put back on every checkout like foreign_keys and query_only.
//
// PRAGMA optimize runs once at startup (SQLite's advice for a long-lived process), as its own best-effort command:
// it may need the write lock, and a locked file must not stall or fail startup.
public sealed class SqliteSynchronousAndOptimizeTests : IDisposable
{
    private const int Full = 2;
    private const int Normal = 1;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"syncopt_{Guid.NewGuid():N}.db");

    // Pooling is on (the provider default), as in production.
    private string ConnectionString => $"Data Source={_path}";

    public SqliteSynchronousAndOptimizeTests()
    {
        using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL; CREATE TABLE t (id INTEGER PRIMARY KEY, v INTEGER); INSERT INTO t VALUES (1, 0);";
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                File.Delete(_path + suffix);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    private static async Task<int> PragmaAsync(IDatabaseContext context, string pragma)
    {
        await using var container = context.CreateSqlContainer($"PRAGMA {pragma}");
        return await container.ExecuteScalarRequiredAsync<int>();
    }

    [Fact]
    public async Task EveryCheckout_RunsWithSynchronousNormal_WhenTheFileIsInWal()
    {
        using var context = new DatabaseContext(ConnectionString, SqliteFactory.Instance);

        Assert.Equal(Normal, await PragmaAsync(context, "synchronous"));
    }

    [Fact]
    public async Task ASynchronousSettingACallerLeftOnTheConnection_IsPutBackOnTheNextCheckout()
    {
        using var context = new DatabaseContext(ConnectionString, SqliteFactory.Instance);

        await using (var poison = context.CreateSqlContainer("PRAGMA synchronous = OFF"))
        {
            await poison.ExecuteNonQueryAsync();
        }

        Assert.Equal(Normal, await PragmaAsync(context, "synchronous"));
    }

    [Fact]
    public async Task AContextThatCannotGetWal_KeepsTheDatabaseDefaultSynchronous()
    {
        // A shared-cache memory database has no WAL ("journal_mode = WAL" answers "memory"); NORMAL is only safe in WAL.
        var name = $"syncopt_mem_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={name};Mode=Memory;Cache=Shared";
        using var sentinel = new SqliteConnection(connectionString);
        sentinel.Open();

        using var context = new DatabaseContext(connectionString, SqliteFactory.Instance);

        Assert.Equal(Full, await PragmaAsync(context, "synchronous"));
    }

    [Fact]
    public void StartupRunsOptimize_AsItsOwnCommand()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=optimize.db", factory);

        var sent = factory.CreatedConnections.SelectMany(c => c.ExecutedNonQueryTexts).ToArray();

        Assert.Contains(sent, sql => sql.Contains("PRAGMA optimize", StringComparison.OrdinalIgnoreCase));
        // Separate from the WAL command: if optimize cannot run, WAL must still have been set.
        Assert.DoesNotContain(sent, sql =>
            sql.Contains("journal_mode", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("optimize", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AFileAnotherConnectionIsWriting_DoesNotStallOrFailStartup()
    {
        await using var holder = new SqliteConnection($"Data Source={_path};Pooling=False");
        await holder.OpenAsync();
        await using (var begin = holder.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE;"; // holds the write lock for the whole test
            await begin.ExecuteNonQueryAsync();
        }

        var clock = Stopwatch.StartNew();
        using var context = new DatabaseContext(ConnectionString, SqliteFactory.Instance);
        clock.Stop();

        // optimize may need the write lock; it waits a couple of seconds at most, not the provider's 30 s default
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10),
            $"startup took {clock.Elapsed.TotalSeconds:F1} s against a locked file");
    }
}
