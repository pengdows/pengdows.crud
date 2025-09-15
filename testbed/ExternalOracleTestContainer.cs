using Oracle.ManagedDataAccess.Client;
using pengdows.crud;

namespace testbed;

/// <summary>
/// Oracle test container that assumes Oracle is already running externally
/// Rather than trying to manage the Oracle container lifecycle
/// </summary>
public class ExternalOracleTestContainer : TestContainer
{
    private readonly string _connectionString;
    private readonly string _testConnectionString;

    public ExternalOracleTestContainer()
    {
        // Use your known working connection string format
        var host = Environment.GetEnvironmentVariable("ORACLE_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("ORACLE_PORT") ?? "1521";
        var service = Environment.GetEnvironmentVariable("ORACLE_SERVICE") ?? "XE";
        var username = Environment.GetEnvironmentVariable("ORACLE_USER") ?? "system";
        var password = Environment.GetEnvironmentVariable("ORACLE_PASSWORD") ?? "mysecretpassword";

        _connectionString = $"User Id={username};Password={password}; Data Source={host}:{port}/{service};";
        _testConnectionString = _connectionString;
    }

    public override async Task StartAsync()
    {
        // Don't start any container - assume Oracle is already running
        // Just test if we can connect
        try
        {
            await using var conn = OracleClientFactory.Instance.CreateConnection();
            if (conn == null)
                throw new InvalidOperationException("Cannot create Oracle connection");
                
            conn.ConnectionString = _testConnectionString;
            await conn.OpenAsync();
            await conn.CloseAsync();
            
            Console.WriteLine("[Oracle] Successfully connected to external Oracle instance");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot connect to external Oracle instance. " +
                $"Make sure Oracle is running and accessible at: {_testConnectionString}. " +
                $"Error: {ex.Message}", ex);
        }
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        var context = new DatabaseContext(_connectionString, OracleClientFactory.Instance, null!);
        return Task.FromResult<IDatabaseContext>(context);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        // Nothing to dispose - we didn't start any containers
        return ValueTask.CompletedTask;
    }
}