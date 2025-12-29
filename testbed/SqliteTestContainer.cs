using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;

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
}
