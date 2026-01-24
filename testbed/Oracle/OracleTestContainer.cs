#region

using DotNet.Testcontainers.Builders;
using Oracle.ManagedDataAccess.Client;
using pengdows.crud;
using IContainer = DotNet.Testcontainers.Containers.IContainer;

#endregion

namespace testbed.Oracle;

public class OracleTestContainer : TestContainer
{
    private const string _password = "mysecurepassword";
    private const string _username = "system";
    private const string _sid = "XEPDB1"; // Oracle 18c XE uses pluggable database
    private const int _port = 1521;
    private readonly IContainer _container;
    private string? _connectionString;

    public OracleTestContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("oracle/database:18.4.0-xe")
            .WithEnvironment("ORACLE_PWD", _password)
            .WithEnvironment("ORACLE_CHARACTERSET", "AL32UTF8")
            .WithExposedPort(_port)
            .WithPortBinding(_port, true) // dynamic host binding
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(_port))
            .WithStartupCallback((container, ct) => Task.Delay(TimeSpan.FromMinutes(2), ct)) // Wait for Oracle initialization
            .Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(_port);

        // Use TNS format for Oracle 18c XE with pluggable database
        _connectionString = $@"User Id={_username};Password={_password};Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT={hostPort}))(CONNECT_DATA=(SERVICE_NAME={_sid})));";
        // // Optional: print logs to debug
        // Console.WriteLine(await _container.());

        // while (_container.Health == TestcontainersHealthStatus.Starting)
        // {
        //     await Task.Delay(1000);
        // }
        // The wait strategy should have already ensured database is ready
        // But add minimal additional wait for connection stability 
        await Task.Delay(5000);
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return Task.FromResult<IDatabaseContext>(
            new DatabaseContext(_connectionString, OracleClientFactory.Instance, null!));
    }

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}
