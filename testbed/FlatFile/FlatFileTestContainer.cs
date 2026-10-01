using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.flatfile;

namespace testbed.FlatFile;

/// <summary>
/// pengdows.flatfile is an embedded, per-directory file engine with no server process (like
/// SQLite/DuckDB) — no container to start/stop, just a scratch directory. DbMode is left at its
/// default (Best): FlatFileDialect now overrides CoerceConnectionMode (see its own remarks),
/// resolving Best to SingleWriter the same way SqliteDialect/DuckDbDialect do — no
/// test-container-level override needed or wanted, since that would just mask a real dialect gap
/// the way the prior explicit DbMode.SingleWriter override here did.
/// </summary>
public class FlatFileTestContainer : TestContainer
{
    private string? _connectionString;
    private string? _directoryPath;

    public override Task StartAsync()
    {
        _directoryPath = Path.Combine(Path.GetTempPath(), $"pengdows.integration.flatfile.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directoryPath);
        _connectionString = $"Path={_directoryPath}";
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
            ConnectionString = _connectionString
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
