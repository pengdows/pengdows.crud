using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.flatfile;

namespace testbed.FlatFile;

/// <summary>
/// pengdows.flatfile is an embedded, per-directory file engine with no server process (like
/// SQLite/DuckDB), so there is no container to start: just a scratch directory. DbMode is left at
/// Best so FlatFileDialect.CoerceConnectionMode decides the mode, the same way the SQLite/DuckDB
/// containers do. Forcing a mode here would hide a dialect gap.
/// </summary>
public class FlatFileTestContainer : TestContainer
{
    private string? _connectionString;
    private string? _directoryPath;

    public override Task StartAsync()
    {
        _directoryPath = Path.Combine(Path.GetTempPath(), $"pengdows.integration.flatfile.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directoryPath);
        _connectionString = $"path={_directoryPath}";
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
            FlatFileProviderFactory.Instance,
            null,
            new TypeMapRegistry());
        return Task.FromResult<IDatabaseContext>(context);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        if (!string.IsNullOrWhiteSpace(_directoryPath))
        {
            try
            {
                if (Directory.Exists(_directoryPath))
                {
                    Directory.Delete(_directoryPath, recursive: true);
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
