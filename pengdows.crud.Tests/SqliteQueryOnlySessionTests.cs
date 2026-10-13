using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// A caller can run PRAGMA query_only = ON through a container, and Microsoft.Data.Sqlite's pool then hands that same
// physical connection to the next checkout, whose write fails with SQLITE_READONLY. foreign_keys is already put back
// on every checkout; query_only is put back OFF the same way, on every checkout and in the same preamble command
// (one round trip, not two). That covers a read checkout as well: SQLite refuses BEGIN IMMEDIATE on a query_only
// connection (SQLITE_READONLY, at BEGIN), and Microsoft.Data.Sqlite begins every transaction with BEGIN IMMEDIATE, so
// a read transaction that inherited a poisoned connection could not even begin.
//
// query_only is never turned ON by the library, for the same reason: read-only is enforced by Mode=ReadOnly in the
// connection string (see SqliteDialect.GetReadOnlyConnectionParameter), which leaves BEGIN IMMEDIATE working.
// ReadOnlyBeginsWork below guards that.
public sealed class SqliteQueryOnlySessionTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"queryonly_{Guid.NewGuid():N}.db");

    // Pooling is on (the provider default), as in production, so a physical connection is handed to the next checkout.
    private string ConnectionString => $"Data Source={_path}";

    public SqliteQueryOnlySessionTests()
    {
        using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "PRAGMA journal_mode = WAL; CREATE TABLE t (id INTEGER PRIMARY KEY, v INTEGER NOT NULL); INSERT INTO t (id, v) VALUES (1, 0);";
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

    // ---- what the preamble says (fakeDb: no real database needed) -----------------------------------------------

    private static string[] SessionSettingsSentOn(bool readOnly)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=test.db", factory);
        using var physical = factory.CreateConnection();
        physical.Open();
        var fake = (fakeDbConnection)physical;

        context.ExecuteSessionSettings(physical, readOnly);

        return fake.ExecutedNonQueryTexts.ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryCheckout_HasQueryOnlyPutBackOff_ReadAndWriteAlike(bool readOnly)
    {
        var sent = SessionSettingsSentOn(readOnly);

        Assert.Contains(sent, sql => sql.Contains("PRAGMA query_only = OFF", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QueryOnlyAndForeignKeys_TravelInOneCommand()
    {
        var sent = SessionSettingsSentOn(readOnly: false);

        Assert.Contains(sent, sql =>
            sql.Contains("PRAGMA foreign_keys = ON", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("PRAGMA query_only = OFF", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoCheckoutIsMadeQueryOnly_BecauseThatBreaksBeginImmediate(bool readOnly)
    {
        var sent = SessionSettingsSentOn(readOnly);

        Assert.DoesNotContain(sent, sql => sql.Contains("query_only = ON", StringComparison.OrdinalIgnoreCase));
    }

    // ---- what it does against real SQLite -----------------------------------------------------------------------

    [Fact]
    public async Task AWriteStillWorks_AfterCodeSetsQueryOnlyOnAConnectionThePoolThenReuses()
    {
        using var context = new DatabaseContext(ConnectionString, SqliteFactory.Instance);

        await using (var poison = context.CreateSqlContainer("PRAGMA query_only = ON"))
        {
            await poison.ExecuteNonQueryAsync();
        }

        await using var write = context.CreateSqlContainer("UPDATE t SET v = 42 WHERE id = 1");
        Assert.Equal(1, await write.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ReadOnlyBeginsWork_ARealReadTransactionStartsAndReads()
    {
        using var context = new DatabaseContext(ConnectionString, SqliteFactory.Instance);

        await using var transaction = context.BeginTransaction(null, ExecutionType.Read);
        await using var read = transaction.CreateSqlContainer("SELECT v FROM t WHERE id = 1");

        Assert.Equal(0, await read.ExecuteScalarRequiredAsync<int>());
    }

    [Fact]
    public async Task AWriteThroughAReadTransaction_IsStillRefused()
    {
        using var context = new DatabaseContext(ConnectionString, SqliteFactory.Instance);

        await using var transaction = context.BeginTransaction(null, ExecutionType.Read);
        await using var write = transaction.CreateSqlContainer("UPDATE t SET v = 9 WHERE id = 1");

        // pengdows.crud refuses it itself, before it reaches SQLite.
        await Assert.ThrowsAsync<ReadOnlyAccessException>(async () => await write.ExecuteNonQueryAsync());
    }
}
