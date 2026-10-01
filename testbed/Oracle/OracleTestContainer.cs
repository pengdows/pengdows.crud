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

    public OracleTestContainer(string? requestedImage = null)
    {
        var image = requestedImage ?? Environment.GetEnvironmentVariable("ORACLE_IMAGE") ?? "gvenzl/oracle-free:23.26.2-slim-faststart";
        var isXe = image.Contains("xe", StringComparison.OrdinalIgnoreCase);
        _sid = isXe ? "XEPDB1" : "FREEPDB1";
        var passwordEnvVar = image.StartsWith("oracle/database:", StringComparison.OrdinalIgnoreCase) ? "ORACLE_PWD" : "ORACLE_PASSWORD";

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
            300); // 300s safety margin; gvenzl/oracle-free:23.26.2-slim-faststart typically starts in ~30s
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