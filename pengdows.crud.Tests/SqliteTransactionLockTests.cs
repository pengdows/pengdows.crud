using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

// The governor serializes writers inside one process, and a write transaction is meant to hold the write lock from
// the moment it begins, so that it cannot fail on its first write because another connection (another process, or a
// context that is not governed with this one) got the lock in between. pengdows.crud does not send BEGIN IMMEDIATE
// itself: Microsoft.Data.Sqlite does, for every isolation level the SQLite dialect asks it for. These tests pin that
// reliance against a real SQLite file, so a provider change that began transactions deferred would fail here.
public sealed class SqliteTransactionLockTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"txlock_{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_path};Pooling=False";

    public SqliteTransactionLockTests()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL; CREATE TABLE t (id INTEGER PRIMARY KEY, v INTEGER);";
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

    // True when another connection can take the write lock right now (one second of patience, no retry loop).
    private async Task<bool> AnotherConnectionCanTakeTheWriteLockAsync()
    {
        await using var other = new SqliteConnection(ConnectionString + ";Default Timeout=1");
        await other.OpenAsync();
        await using var command = other.CreateCommand();
        command.CommandText = "BEGIN IMMEDIATE; ROLLBACK;";
        try
        {
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
        {
            return false;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(IsolationLevel.ReadCommitted)]
    [InlineData(IsolationLevel.Serializable)]
    public async Task AWriteTransaction_HoldsTheWriteLockFromBegin_BeforeItHasWrittenAnything(IsolationLevel? level)
    {
        using var context = new DatabaseContext(ConnectionString, SqliteFactory.Instance);

        await using var tx = context.BeginTransaction(level, ExecutionType.Write);

        Assert.False(await AnotherConnectionCanTakeTheWriteLockAsync());
    }

    [Fact]
    public async Task OnceTheWriteTransactionEnds_TheWriteLockIsFree()
    {
        using var context = new DatabaseContext(ConnectionString, SqliteFactory.Instance);

        await using (var tx = context.BeginTransaction(null, ExecutionType.Write))
        {
            tx.Rollback();
        }

        Assert.True(await AnotherConnectionCanTakeTheWriteLockAsync());
    }

    [Fact]
    public async Task AReadTransaction_DoesNotBlockAWriter()
    {
        using var context = new DatabaseContext(ConnectionString, SqliteFactory.Instance);

        await using var tx = context.BeginTransaction(null, ExecutionType.Read);

        Assert.True(await AnotherConnectionCanTakeTheWriteLockAsync());
    }
}
