using System.Data;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Real-provider coverage for Firebird Embedded's multiple-attachment behavior.
/// These tests do not use Docker: FirebirdSql.Data.FirebirdClient opens the temporary
/// database through its embedded engine in the current process.
/// </summary>
public sealed class FirebirdEmbeddedConnectionTests
{
    [SkippableFact]
    [Trait("Category", "FirebirdEmbedded")]
    public async Task FirebirdEmbedded_AllowsMultipleSimultaneousAttachments()
    {
        var path = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(path);
            using var context = CreateContext(path);
            using var first = context.GetConnection(ExecutionType.Read);
            using var second = context.GetConnection(ExecutionType.Read);

            Assert.Equal(ConnectionState.Open, first.State);
            Assert.Equal(ConnectionState.Open, second.State);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [SkippableFact]
    [Trait("Category", "FirebirdEmbedded")]
    public async Task FirebirdEmbedded_ConcurrentReadersUseSeparateAttachments()
    {
        var path = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(path);
            using var context = CreateContext(path);

            var reads = await Task.WhenAll(
                ReadCountAsync(context),
                ReadCountAsync(context));

            Assert.Equal(2, reads.Length);
            Assert.All(reads, count => Assert.Equal(1, count));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [SkippableFact]
    [Trait("Category", "FirebirdEmbedded")]
    public async Task FirebirdEmbedded_ReaderAndWriterCanOperateConcurrently()
    {
        var path = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(path);
            using var context = CreateContext(path);

            var operations = await Task.WhenAll(
                ReadCountAsync(context),
                InsertAsync(context, 2));

            Assert.Equal(1, operations[0]);
            Assert.Equal(1, operations[1]);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [SkippableFact]
    [Trait("Category", "FirebirdEmbedded")]
    public async Task FirebirdEmbedded_NonConflictingWritersCanOperateConcurrently()
    {
        var path = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(path);
            using var context = CreateContext(path);

            await using (var seed = context.CreateSqlContainer(
                       "INSERT INTO test_rows (id, row_value) VALUES (2, 1)"))
            {
                Assert.Equal(1, await seed.ExecuteNonQueryAsync());
            }

            await Task.WhenAll(
                UpdateAsync(context, 1),
                UpdateAsync(context, 2));

            await using var verify = context.CreateSqlContainer("SELECT SUM(row_value) FROM test_rows");
            Assert.Equal(4L, await verify.ExecuteScalarRequiredAsync<long>());
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [SkippableFact]
    [Trait("Category", "FirebirdEmbedded")]
    public async Task FirebirdEmbedded_BestModeUsesPreventDatabaseUnloadAndRealWorkAttachments()
    {
        var path = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(path);
            var config = new DatabaseContextConfiguration
            {
                ConnectionString = ConnectionString(path, disablePooling: false),
                ProviderName = SupportedDatabase.Firebird.ToString(),
                DbMode = DbMode.Best
            };

            using var context = new DatabaseContext(
                config,
                FirebirdClientFactory.Instance,
                NullLoggerFactory.Instance);

            Assert.Equal(SupportedDatabase.Firebird, context.Product);
            Assert.Equal(DbMode.PreventDatabaseUnload, context.ConnectionMode);
            var sentinels = context.GetSentinelSnapshot();
            Assert.Equal(2, sentinels.Count);
            Assert.Contains(sentinels, sentinel => sentinel.ExecutionType == ExecutionType.Read);
            Assert.Contains(sentinels, sentinel => sentinel.ExecutionType == ExecutionType.Write);

            using var read = context.GetConnection(ExecutionType.Read);
            using var write = context.GetConnection(ExecutionType.Write);
            Assert.Equal(ConnectionState.Open, read.State);
            Assert.Equal(ConnectionState.Open, write.State);
            Assert.NotSame(read, write);
        }
        catch (DllNotFoundException ex)
        {
            throw new SkipException($"Firebird Embedded native library is unavailable: {ex.Message}");
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static async Task CreateDatabaseAsync(string path)
    {
        try
        {
            FbConnection.CreateDatabase(ConnectionString(path));
            await using var connection = await OpenAsync(path);
            await using var create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE test_rows (id INTEGER NOT NULL PRIMARY KEY, row_value INTEGER NOT NULL)";
            await create.ExecuteNonQueryAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO test_rows (id, row_value) VALUES (1, 1)";
            await insert.ExecuteNonQueryAsync();
        }
        catch (SkipException)
        {
            throw;
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException)
        {
            throw new SkipException($"Firebird Embedded is unavailable: {ex.Message}");
        }
    }

    private static async Task<FbConnection> OpenAsync(string path)
    {
        var connection = new FbConnection(ConnectionString(path));
        try
        {
            await connection.OpenAsync();
            return connection;
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException)
        {
            await connection.DisposeAsync();
            throw new SkipException($"Firebird Embedded is unavailable: {ex.Message}");
        }
    }

    private static DatabaseContext CreateContext(string path)
    {
        return new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = ConnectionString(path, disablePooling: false),
                ProviderName = SupportedDatabase.Firebird.ToString(),
                DbMode = DbMode.PreventDatabaseUnload
            },
            FirebirdClientFactory.Instance,
            NullLoggerFactory.Instance);
    }

    private static async Task<int> ReadCountAsync(DatabaseContext context)
    {
        await using var container = context.CreateSqlContainer("SELECT COUNT(*) FROM test_rows");
        return await container.ExecuteScalarRequiredAsync<int>();
    }

    private static async Task<int> InsertAsync(DatabaseContext context, int id)
    {
        await using var container = context.CreateSqlContainer(
            $"INSERT INTO test_rows (id, row_value) VALUES ({id}, 1)");
        return await container.ExecuteNonQueryAsync();
    }

    private static async Task UpdateAsync(DatabaseContext context, int id)
    {
        await using var container = context.CreateSqlContainer(
            $"UPDATE test_rows SET row_value = row_value + 1 WHERE id = {id}");
        Assert.Equal(1, await container.ExecuteNonQueryAsync());
    }

    private static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"pengdows_firebird_embedded_{Guid.NewGuid():N}.fdb");

    private static string ConnectionString(string path, bool disablePooling = true) =>
        $"Database={path};ServerType=Embedded;ClientLibrary={ClientLibrary};" +
        $"User ID=SYSDBA;Password={Password};" +
        (disablePooling ? "Pooling=false" : string.Empty);

    private static string ClientLibrary =>
        Environment.GetEnvironmentVariable("FIREBIRD_EMBEDDED_CLIENT_LIBRARY")
        ?? "/opt/firebird/lib/libfbclient.so";

    private static string Password
    {
        get
        {
            var password = Environment.GetEnvironmentVariable("FIREBIRD_TEST_PASSWORD");
            return string.IsNullOrWhiteSpace(password)
                ? throw new SkipException(
                    "Firebird Embedded credentials are unavailable; set FIREBIRD_TEST_PASSWORD " +
                    "by running scripts/install-firebird-embedded.sh first.")
                : password;
        }
    }

    private static void DeleteDatabase(string path)
    {
        try
        {
            FbConnection.ClearAllPools();
        }
        catch
        {
            // Best effort: the provider may not have loaded or may already be shutting down.
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort: Firebird may still hold a file handle while a skipped test unwinds.
        }
    }
}
