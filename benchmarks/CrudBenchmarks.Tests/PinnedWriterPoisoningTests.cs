using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.exceptions;

namespace CrudBenchmarks.Tests;

// A pinned writer connection is the common way to serialize SQLite writes, and it is the right thing to compare
// pengdows.crud against. This is the SESSION-POISONING fault, which needs a real SQLite file because what is being
// tested is a real pragma on a real, pooled connection: some code turns foreign key enforcement off on a connection.
// The other fault, a connection dying, needs no real database and lives in fakeDb: see
// BrokenConnectionRecoveryTests (pengdows.crud.Tests) and SqliteFileWriterPoisonBenchmarks (the pinned writer under the
// same break).
public sealed class PinnedWriterPoisoningTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"poison_{Guid.NewGuid():N}.db");
    private string PlainConnectionString => $"Data Source={_path};Pooling=False";
    private string ForeignKeysOnConnectionString => $"Data Source={_path};Pooling=False;Foreign Keys=True";

    public PinnedWriterPoisoningTests()
    {
        using var connection = new SqliteConnection(PlainConnectionString);
        connection.Open();
        connection.Execute(@"
            PRAGMA journal_mode = WAL;
            CREATE TABLE t (id INTEGER PRIMARY KEY, v INTEGER NOT NULL);
            CREATE TABLE parent (id INTEGER PRIMARY KEY);
            CREATE TABLE child (id INTEGER PRIMARY KEY AUTOINCREMENT, parent_id INTEGER NOT NULL REFERENCES parent(id));
            INSERT INTO parent (id) VALUES (1);
            INSERT INTO t (id, v) VALUES (1, 0), (2, 0), (3, 0);");
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

    private static async Task InsertOrphanChildAsync(IDatabaseContext context)
    {
        await using var container = context.CreateSqlContainer();
        container.Query.Append("INSERT INTO child (parent_id) VALUES (");
        container.Query.Append(container.MakeParameterName("p"));
        container.Query.Append(")");
        container.AddParameterWithValue("p", DbType.Int32, 999);
        await container.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAsync(string table)
    {
        await using var connection = new SqliteConnection(PlainConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table}");
    }

    // ---- code on the connection changes its session state -----------------------------------------------------

    [Fact]
    public async Task PinnedWriter_KeepsForeignKeysOff_AfterAnyCodeOnItTurnsThemOff()
    {
        await using var pinned = new SqliteConnection(ForeignKeysOnConnectionString);
        await pinned.OpenAsync();

        // Right after it opens the pinned connection enforces foreign keys...
        await Assert.ThrowsAnyAsync<SqliteException>(() =>
            pinned.ExecuteAsync("INSERT INTO child (parent_id) VALUES (999)"));
        Assert.Equal(0, await CountAsync("child"));

        // ...until anything run on it turns that off. The connection never leaves, so neither does the setting.
        await pinned.ExecuteAsync("PRAGMA foreign_keys = OFF");
        await pinned.ExecuteAsync("INSERT INTO child (parent_id) VALUES (999)");
        await pinned.ExecuteAsync("INSERT INTO child (parent_id) VALUES (998)");

        Assert.Equal(2, await CountAsync("child")); // two orphan rows persisted, no error raised
    }

    // ---- the same poison through Dapper, pinned and per-operation ----------------------------------------------

    [Fact]
    public async Task PinnedWriter_FailsEveryLaterWrite_AfterCodeSetsQueryOnlyOnIt()
    {
        await using var pinned = new SqliteConnection(ForeignKeysOnConnectionString);
        await pinned.OpenAsync();

        await pinned.ExecuteAsync("PRAGMA query_only = ON");

        // The connection never leaves, so neither does the setting: nothing re-asserts it.
        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAnyAsync<SqliteException>(() =>
                pinned.ExecuteAsync("UPDATE t SET v = @v WHERE id = 1", new { v = i }));
        }
    }

    [Fact]
    public async Task PerOperationDapper_InheritsQueryOnlyFromThePool_BecauseNothingPutsItBack()
    {
        // Microsoft.Data.Sqlite pools by default; the next new SqliteConnection(...).Open() gets the physical
        // connection the last caller returned, with whatever that caller left set.
        var pooled = ForeignKeysOnConnectionString.Replace(";Pooling=False", string.Empty);

        await using (var poisoner = new SqliteConnection(pooled))
        {
            await poisoner.OpenAsync();
            await poisoner.ExecuteAsync("PRAGMA query_only = ON");
        }

        await using var next = new SqliteConnection(pooled);
        await next.OpenAsync();
        await Assert.ThrowsAnyAsync<SqliteException>(() =>
            next.ExecuteAsync("UPDATE t SET v = 7 WHERE id = 1"));
    }

    [Fact]
    public async Task PerOperationDapper_WithForeignKeysTrue_PutsForeignKeysBackOnEveryOpen()
    {
        // Foreign Keys=True makes Microsoft.Data.Sqlite send the pragma on every Open(), pooled or not, so this
        // poison does not survive a new connection the way query_only does: there is a keyword for one, not the other.
        var pooled = ForeignKeysOnConnectionString.Replace(";Pooling=False", string.Empty);

        await using (var poisoner = new SqliteConnection(pooled))
        {
            await poisoner.OpenAsync();
            await poisoner.ExecuteAsync("PRAGMA foreign_keys = OFF");
        }

        await using var next = new SqliteConnection(pooled);
        await next.OpenAsync();
        await Assert.ThrowsAnyAsync<SqliteException>(() =>
            next.ExecuteAsync("INSERT INTO child (parent_id) VALUES (999)"));
        Assert.Equal(0, await CountAsync("child"));
    }

    // Pooling is ON here, as in production: the provider hands a returned physical connection to the next checkout,
    // so a setting changed on it by one caller is still there for the next. With Pooling=False every checkout would
    // be a brand new connection and this test would pass whether or not anything re-asserted the setting.
    private string PooledConnectionString => $"Data Source={_path}";

    [Fact]
    public async Task Pengdows_ReassertsForeignKeysOnEveryCheckout_EvenWhenThePoolHandsBackThePoisonedConnection()
    {
        using var context = new DatabaseContext(PooledConnectionString, SqliteFactory.Instance);

        await using (var container = context.CreateSqlContainer("PRAGMA foreign_keys = OFF"))
        {
            await container.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<ForeignKeyViolationException>(() => InsertOrphanChildAsync(context));
        Assert.Equal(0, await CountAsync("child"));
    }
}
