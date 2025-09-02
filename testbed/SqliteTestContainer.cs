using Microsoft.Data.Sqlite;
using pengdows.crud;

namespace testbed;

public class SqliteTestContainer : TestContainer
{
    private string? _connectionString;

    public override Task StartAsync()
    {
        _connectionString = "Data Source=:memory:";
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
            SqliteFactory.Instance,
            null!);
        return Task.FromResult<IDatabaseContext>(context);
    }
}