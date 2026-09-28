using Microsoft.Data.Sqlite;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// A SQLite database on its own randomly named file, for a test that needs a SQLite context of its
/// own. There must never be a second <see cref="DatabaseContext"/> on the fixture's file (one context
/// per connection string, especially for file-based databases), so such a test gets a separate file,
/// deleted again on dispose.
/// </summary>
internal sealed class IsolatedSqliteDatabase : IDisposable
{
    public IsolatedSqliteDatabase()
    {
        FilePath = Path.Combine(Path.GetTempPath(), $"pengdows.isolated.{Guid.NewGuid():N}.db");
        Context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={FilePath}",
            DbMode = DbMode.Best
        }, SqliteFactory.Instance);
    }

    public string FilePath { get; }

    public IDatabaseContext Context { get; }

    public void Dispose()
    {
        Context.Dispose();
        // Microsoft.Data.Sqlite pools connections, which keep the file open.
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { FilePath, FilePath + "-wal", FilePath + "-shm", FilePath + "-journal" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best effort: a temp file left behind does not affect other tests.
            }
        }
    }
}
