#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using pengdows.crud;

#endregion

namespace testbed.SqlServer;

public class SqlServerTestContainer : TestContainer
{
    private IContainer _container;
    private readonly Func<IContainer> _buildContainer;
    private string? _connectionString;
    // Random per instance so two processes sharing one physical container never see each other's
    // tables, even if they run concurrently. SQL Server needs no separate fixed "bootstrap"
    // database - master already exists - so unlike MySQL/Postgres there's no env-var value that
    // would break WithReuse's config hash here.
    private readonly string _database = "crud_test_" + TestContainerReuse.NewSuffix();
    private string _password = "YourPassword123";
    private string _username = "sa";
    private readonly string _image;

    //docker run -e 'ACCEPT_EULA=Y' -e 'MSSQL_SA_PASSWORD=YourPassword123' -p 1433:1433 --name sql_server_container -d mcr.microsoft.com/mssql/server

    // var sqlConnectionString =
    //     "Server=localhost;uid=sa;pwd=YourPassword123;Initial Catalog=testdb;TrustServerCertificate=true";
    public SqlServerTestContainer(string? image = null)
    {
        _image = image ?? TestbedImageMatrix.Latest("SQL Server");
        _buildContainer = () =>
        {
            var builder = new ContainerBuilder()
                .WithImage(_image)
                .WithEnvironment("MSSQL_SA_PASSWORD", _password)
                .WithEnvironment("ACCEPT_EULA", "Y")
                .WithPortBinding(1433, true);

            if (TestContainerReuse.Enabled)
            {
                builder = builder.WithReuse(true);
            }

            return builder.Build();
        };
        _container = _buildContainer();
    }


    public override async Task StartAsync()
    {
        // sqlservr occasionally dies with a fatal-error core dump while it starts (the container exits and nothing
        // ever answers). That is the vendor image, not the library under test: a container that is no longer
        // running is replaced by a fresh one, up to three tries; one that is running and never answers still fails.
        const int attempts = 3;
        string connectionString;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                connectionString = await StartContainerAndWaitAsync();
                break;
            }
            catch (Exception) when (attempt < attempts && _container.State != TestcontainersStates.Running)
            {
                Console.WriteLine($"  [retry] SQL Server container exited while starting (attempt {attempt} of {attempts})");
                await _container.DisposeAsync();
                _container = _buildContainer();
            }
        }

        await createNewDb(connectionString);
    }

    private async Task<string> StartContainerAndWaitAsync()
    {
        if (TestContainerReuse.Enabled)
        {
            using var _ = await TestContainerReuse.AcquireStartupLockAsync("sqlserver-" + _image);
            await _container.StartAsync();
        }
        else
        {
            await _container.StartAsync();
        }

        var hostPort = _container.GetMappedPublicPort(1433);
        var tmp =
            $@"Server=localhost,{hostPort};uid={_username};pwd={_password};Initial Catalog=master;TrustServerCertificate=true;Connection Timeout=1";
        // SQL Server's first start (system database recovery) intermittently took longer than the
        // default 60s under full-suite load, in both the testbed and the integration suite. Wait in 10 s slices up to
        // 180 s, and give up early when the container has exited.
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (true)
        {
            if (_container.State != TestcontainersStates.Running)
            {
                throw new InvalidOperationException("The SQL Server container exited while starting.");
            }

            try
            {
                await WaitForDbToStart(SqlClientFactory.Instance, tmp, _container, 10);
                return tmp;
            }
            catch (TimeoutException) when (DateTime.UtcNow < deadline)
            {
            }
        }
    }

    private async Task createNewDb(string connectionString)
    {
        var factory = SqlClientFactory.Instance;
        var connection = factory.CreateConnection();
        var csb = factory.CreateConnectionStringBuilder();
        //this gyration parses and scrubs the connection string.
        csb.ConnectionString = connectionString;
        connection.ConnectionString = csb.ConnectionString;

        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID('{_database}') IS NULL CREATE DATABASE [{_database}]";
        await command.ExecuteNonQueryAsync();
        csb["Initial Catalog"] = _database;
        _connectionString = csb.ConnectionString;
        await connection.CloseAsync();
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return Task.FromResult<IDatabaseContext>(
            new DatabaseContext(_connectionString, SqlClientFactory.Instance, new TypeMapRegistry()));
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_connectionString is not null)
        {
            try
            {
                var hostPort = _container.GetMappedPublicPort(1433);
                var adminConnectionString =
                    $@"Server=localhost,{hostPort};uid={_username};pwd={_password};Initial Catalog=master;TrustServerCertificate=true;Connection Timeout=15";
                var factory = SqlClientFactory.Instance;
                await using var connection = factory.CreateConnection()!;
                connection.ConnectionString = adminConnectionString;
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"IF DB_ID('{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END";
                await command.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best-effort: a shared/reused container may already be gone.
            }
        }

        if (!TestContainerReuse.Enabled)
        {
            await _container.DisposeAsync();
        }
    }
}