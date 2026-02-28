#region

using DuckDB.NET.Data;
using pengdows.crud;

#endregion

namespace testbed.DuckDb;

public class DuckDbTestContainer : TestContainer
{
    private string? _connectionString;
    private string? _dbFilePath;

    public override Task StartAsync()
    {
        _dbFilePath = Path.Combine(Path.GetTempPath(), $"pengdows.integration.duckdb.{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbFilePath}";
        return Task.CompletedTask;
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString == null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        var context = new DatabaseContext(
            _connectionString,
            DuckDBClientFactory.Instance);
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