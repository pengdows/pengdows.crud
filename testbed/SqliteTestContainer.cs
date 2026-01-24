using System.IO;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace testbed;

public class SqliteTestContainer : TestContainer
{
    private string? _connectionString;
    private string? _dbFilePath;

    public override Task StartAsync()
    {
        _dbFilePath = Path.Combine(Path.GetTempPath(), $"pengdows.integration.{Guid.NewGuid():N}.sqlite");
        _connectionString = $"Data Source={_dbFilePath}";
        return Task.CompletedTask;
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString == null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = _connectionString,
            DbMode = DbMode.Best
        };
        var context = new DatabaseContext(
            config,
            SqliteFactory.Instance,
            null,
            new TypeMapRegistry());
        return Task.FromResult<IDatabaseContext>(context);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        if (!string.IsNullOrWhiteSpace(_dbFilePath))
        {
            try
            {
                if (File.Exists(_dbFilePath))
                {
                    File.Delete(_dbFilePath);
                }
            }
            catch
            {
                // best-effort cleanup; ignore failures
            }
        }

        return ValueTask.CompletedTask;
    }
}
