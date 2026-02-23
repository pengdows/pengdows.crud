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
    private const int _port = 1521;
    private readonly IContainer _container;
    private string? _connectionString;
    private readonly string _sid;

    public OracleTestContainer()
    {
        var imageType = Environment.GetEnvironmentVariable("ORACLE_IMAGE_TYPE")?.ToLower() ?? "free";
        string image;
        string passwordEnvVar;

        if (imageType == "xe")
        {
            image = "oracle/database:18.4.0-xe";
            _sid = "XEPDB1";
            passwordEnvVar = "ORACLE_PWD";
        }
        else
        {
            // Default: gvenzl/oracle-free:slim — smaller, faster startup than oracle/database:18.4.0-xe
            image = Environment.GetEnvironmentVariable("ORACLE_IMAGE") ?? "gvenzl/oracle-free:slim";
            _sid = "FREEPDB1";
            passwordEnvVar = "ORACLE_PASSWORD";
        }

        Console.WriteLine($"[Oracle] Using image: {image} (SID: {_sid})");

        _container = new ContainerBuilder()
            .WithImage(image)
            .WithEnvironment(passwordEnvVar, _password)
            .WithEnvironment("ORACLE_CHARACTERSET", "AL32UTF8")
            .WithExposedPort(_port)
            .WithPortBinding(_port, true) // dynamic host binding
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(_port))
            .Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(_port);

        // Use TNS format for Oracle pluggable database
        _connectionString =
            $@"User Id={_username};Password={_password};Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT={hostPort}))(CONNECT_DATA=(SERVICE_NAME={_sid})));";

        // Wait for Oracle to be truly ready for connections
        await WaitForDbToStart(OracleClientFactory.Instance, _connectionString, _container,
            300); // Oracle can be slow to start
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return Task.FromResult<IDatabaseContext>(
            new DatabaseContext(_connectionString, OracleClientFactory.Instance));
    }

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}