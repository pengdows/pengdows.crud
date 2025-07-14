#region

using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using pengdows.crud;
using IContainer = DotNet.Testcontainers.Containers.IContainer;

#endregion

namespace testbed;

public class OracleTestContainer : TestContainer
{
    private const string _password = "mysecurepassword";
    private const string _username = "system";
    private const string _sid = "XE"; // confirmed from inspect (ORACLE_SID=XE)
    private const int _port = 1521;
    private readonly IContainer _container;
    private string? _connectionString;

    public OracleTestContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("oracle/database:18.4.0-xe")
            .WithEnvironment("ACCEPT_LICENSE_AGREEMENT", "Y")
            .WithEnvironment("ORACLE_PWD", _password)
            .WithExposedPort(_port)
            .WithPortBinding(_port, true) // dynamic host binding
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1521))
            .Build();
        throw new Exception("won't start");
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(_port);

        // Fully qualified TNS format — safest and matches official documentation
        //         _connectionString = $@"
        // User Id={_username};
        // Password={_password};
        // Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT={hostPort}))
        // (CONNECT_DATA=(SERVICE_NAME={_sid})))";
        var csb = OracleClientFactory.Instance.CreateConnectionStringBuilder();
        csb.ConnectionString = $"User Id={_username};Password={_password}; Data Source=localhost:{hostPort}/{_sid};";
        _connectionString = csb.ConnectionString;
        // // Optional: print logs to debug
        // Console.WriteLine(await _container.());

        // while (_container.Health == TestcontainersHealthStatus.Starting)
        // {
        //     await Task.Delay(1000);
        // }
        // Wait until the DB is accepting connections and SELECT 1 succeeds
        await WaitForDbToStart(OracleClientFactory.Instance, _connectionString, _container, 300);
    }

    public override async Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return new DatabaseContext(_connectionString, OracleClientFactory.Instance,
            services.GetRequiredService<ITypeMapRegistry>());
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}