#region

using DuckDB.NET.Data;
using pengdows.crud;

#endregion

namespace testbed;

public class DuckDbTestContainer : TestContainer
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
            DuckDBClientFactory.Instance,
            null);
        return Task.FromResult<IDatabaseContext>(context);
    }
}
