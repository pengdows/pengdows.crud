using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REL-005: <c>PRAGMA journal_mode = WAL</c> is stored in the database file, so it runs once per
/// context (on its initialization connection), not on every checkout — profiled at about 1.8 us per
/// operation on SQLite. <c>PRAGMA foreign_keys = ON</c> is per-connection state and still runs on
/// every checkout.
/// </summary>
public class SqliteJournalModeOncePerContextTests
{
    private static List<string> AllExecutedTexts(fakeDbFactory factory) =>
        factory.CreatedConnections.SelectMany(c => c.ExecutedReaderTexts.Concat(c.ExecutedNonQueryTexts)).ToList();

    private static async Task RunWritesAsync(DatabaseContext ctx, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await using var sc = ctx.CreateSqlContainer("UPDATE t SET v = 1");
            await sc.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData(DbMode.Standard)]
    [InlineData(DbMode.SingleWriter)]
    public async Task JournalMode_RunsOncePerContext_ForeignKeysOnEveryCheckout(DbMode mode)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=wal.db;EmulatedProduct=Sqlite",
            DbMode = mode
        };
        await using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        await RunWritesAsync(ctx, 3);

        var texts = AllExecutedTexts(factory);
        Assert.Equal(1, texts.Count(t => t.Contains("journal_mode", StringComparison.OrdinalIgnoreCase)));
        Assert.True(texts.Count(t => t.Contains("foreign_keys", StringComparison.OrdinalIgnoreCase)) >= 3,
            string.Join(" | ", texts));
    }

    [Fact]
    public async Task RealSqliteFile_IsInWalModeAfterConstruction()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pengdows-wal-{Guid.NewGuid():N}.db");
        try
        {
            var config = new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={path}",
                DbMode = DbMode.Standard
            };
            await using (var ctx = new DatabaseContext(config, Microsoft.Data.Sqlite.SqliteFactory.Instance,
                             NullLoggerFactory.Instance))
            {
                await using var sc = ctx.CreateSqlContainer("PRAGMA journal_mode");
                Assert.Equal("wal", await sc.ExecuteScalarRequiredAsync<string>());
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                System.IO.File.Delete(path + suffix);
            }
        }
    }

    [Fact]
    public async Task ReadOnlyContext_NeverSetsJournalMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=wal.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        await using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        await using (var sc = ctx.CreateSqlContainer("SELECT 1"))
        {
            await sc.ExecuteScalarOrNullAsync<int>();
        }

        var texts = AllExecutedTexts(factory);
        Assert.False(texts.Any(t => t.Contains("journal_mode", StringComparison.OrdinalIgnoreCase)),
            string.Join(" | ", texts));
    }
}
