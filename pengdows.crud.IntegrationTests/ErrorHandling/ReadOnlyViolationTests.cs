using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

/// <summary>
/// Proves that writing to a file-based read-only SQLite or DuckDB connection
/// produces a <see cref="ReadOnlyViolationException"/> rather than an opaque
/// generic error.
/// </summary>
/// <remarks>
/// These tests bypass the application-layer guard (<c>ReadWriteMode.ReadOnly</c> check)
/// by constructing the context with the default <c>ReadWriteMode.ReadWrite</c> while
/// embedding the read-only flag directly in the connection string. This simulates the
/// scenario where a user passes a pre-built connection string that contains
/// <c>Mode=ReadOnly</c> (SQLite) or <c>access_mode=READ_ONLY</c> (DuckDB) without
/// going through the framework's dedicated read-only context path — e.g. when reusing
/// a connection string from an external source.
/// </remarks>
public class ReadOnlyViolationTests
{
    // ── SQLite ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sqlite_InsertOnReadOnlyConnection_Throws_ReadOnlyViolationException()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"pengdows_ro_test_{Guid.NewGuid():N}.sqlite");
        try
        {
            // Arrange: create the database file and a table using a normal write context.
            await using (var writeCtx = new DatabaseContext(
                             $"Data Source={dbPath}",
                             SqliteFactory.Instance))
            {
                await using var setup = writeCtx.CreateSqlContainer(
                    "CREATE TABLE IF NOT EXISTS ro_test (id INTEGER PRIMARY KEY, name TEXT)");
                await setup.ExecuteNonQueryAsync();
            }

            // Act: open the same file with Mode=ReadOnly.
            // ReadWriteMode defaults to ReadWrite so the application guard does not fire;
            // the INSERT reaches SQLite which rejects it with SQLITE_READONLY (error code 8).
            var config = new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={dbPath};Mode=ReadOnly",
                DbMode = DbMode.Standard   // avoid SingleWriter opening a separate write connection
            };
            await using var roCtx = new DatabaseContext(config, SqliteFactory.Instance, null, new TypeMapRegistry());

            await using var insertSc = roCtx.CreateSqlContainer(
                "INSERT INTO ro_test (id, name) VALUES (1, 'test')");

            // Assert
            await Assert.ThrowsAsync<ReadOnlyViolationException>(
                async () => await insertSc.ExecuteNonQueryAsync());
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task Sqlite_UpdateOnReadOnlyConnection_Throws_ReadOnlyViolationException()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"pengdows_ro_test_{Guid.NewGuid():N}.sqlite");
        try
        {
            // Arrange
            await using (var writeCtx = new DatabaseContext(
                             $"Data Source={dbPath}",
                             SqliteFactory.Instance))
            {
                await using var setup = writeCtx.CreateSqlContainer(
                    "CREATE TABLE IF NOT EXISTS ro_test (id INTEGER PRIMARY KEY, name TEXT)");
                await setup.ExecuteNonQueryAsync();
                await using var seed = writeCtx.CreateSqlContainer(
                    "INSERT INTO ro_test (id, name) VALUES (42, 'original')");
                await seed.ExecuteNonQueryAsync();
            }

            var config = new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={dbPath};Mode=ReadOnly",
                DbMode = DbMode.Standard
            };
            await using var roCtx = new DatabaseContext(config, SqliteFactory.Instance, null, new TypeMapRegistry());
            await using var updateSc = roCtx.CreateSqlContainer(
                "UPDATE ro_test SET name = 'changed' WHERE id = 42");

            // Assert
            await Assert.ThrowsAsync<ReadOnlyViolationException>(
                async () => await updateSc.ExecuteNonQueryAsync());
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task Sqlite_ReadOnReadOnlyConnection_Succeeds()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"pengdows_ro_test_{Guid.NewGuid():N}.sqlite");
        try
        {
            // Arrange
            await using (var writeCtx = new DatabaseContext(
                             $"Data Source={dbPath}",
                             SqliteFactory.Instance))
            {
                await using var setup = writeCtx.CreateSqlContainer(
                    "CREATE TABLE IF NOT EXISTS ro_test (id INTEGER PRIMARY KEY, name TEXT)");
                await setup.ExecuteNonQueryAsync();
                await using var seed = writeCtx.CreateSqlContainer(
                    "INSERT INTO ro_test (id, name) VALUES (1, 'hello')");
                await seed.ExecuteNonQueryAsync();
            }

            var config = new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={dbPath};Mode=ReadOnly",
                DbMode = DbMode.Standard
            };
            await using var roCtx = new DatabaseContext(config, SqliteFactory.Instance, null, new TypeMapRegistry());

            // Act: a SELECT should succeed on a read-only connection
            await using var selectSc = roCtx.CreateSqlContainer("SELECT COUNT(*) FROM ro_test");
            var count = await selectSc.ExecuteScalarRequiredAsync<long>();

            Assert.Equal(1L, count);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    // ── DuckDB ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DuckDb_InsertOnReadOnlyConnection_Throws_ReadOnlyViolationException()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"pengdows_ro_test_{Guid.NewGuid():N}.duckdb");
        try
        {
            // Arrange: create the database file and a table using a normal write context.
            await using (var writeCtx = new DatabaseContext(
                             $"Data Source={dbPath}",
                             DuckDBClientFactory.Instance))
            {
                await using var setup = writeCtx.CreateSqlContainer(
                    "CREATE TABLE IF NOT EXISTS ro_test (id INTEGER PRIMARY KEY, name TEXT)");
                await setup.ExecuteNonQueryAsync();
            }

            // Act: open the same file with access_mode=READ_ONLY.
            // ReadWriteMode defaults to ReadWrite so the application guard does not fire;
            // the INSERT reaches DuckDB which rejects it (SQLSTATE 25006 or read-only message).
            var config = new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={dbPath};access_mode=READ_ONLY",
                DbMode = DbMode.Standard
            };
            await using var roCtx = new DatabaseContext(config, DuckDBClientFactory.Instance, null, new TypeMapRegistry());

            await using var insertSc = roCtx.CreateSqlContainer(
                "INSERT INTO ro_test (id, name) VALUES (1, 'test')");

            // Assert
            await Assert.ThrowsAsync<ReadOnlyViolationException>(
                async () => await insertSc.ExecuteNonQueryAsync());
        }
        finally
        {
            TryDeleteFile(dbPath);
            TryDeleteFile(dbPath + ".wal");
        }
    }

    [Fact]
    public async Task DuckDb_ReadOnReadOnlyConnection_Succeeds()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"pengdows_ro_test_{Guid.NewGuid():N}.duckdb");
        try
        {
            // Arrange
            await using (var writeCtx = new DatabaseContext(
                             $"Data Source={dbPath}",
                             DuckDBClientFactory.Instance))
            {
                await using var setup = writeCtx.CreateSqlContainer(
                    "CREATE TABLE IF NOT EXISTS ro_test (id INTEGER PRIMARY KEY, name TEXT)");
                await setup.ExecuteNonQueryAsync();
                await using var seed = writeCtx.CreateSqlContainer(
                    "INSERT INTO ro_test VALUES (1, 'hello')");
                await seed.ExecuteNonQueryAsync();
            }

            var config = new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source={dbPath};access_mode=READ_ONLY",
                DbMode = DbMode.Standard
            };
            await using var roCtx = new DatabaseContext(config, DuckDBClientFactory.Instance, null, new TypeMapRegistry());

            // Act: a SELECT should succeed on a read-only connection
            await using var selectSc = roCtx.CreateSqlContainer("SELECT COUNT(*) FROM ro_test");
            var count = await selectSc.ExecuteScalarRequiredAsync<long>();

            Assert.Equal(1L, count);
        }
        finally
        {
            TryDeleteFile(dbPath);
            TryDeleteFile(dbPath + ".wal");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
