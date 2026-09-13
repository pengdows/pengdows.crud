#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using pengdows.crud;

#endregion

namespace testbed.SqlServer;

public class SqlServerTestContainer : TestContainer
{
    private readonly IContainer _container;
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
        _image = image ?? "mcr.microsoft.com/mssql/server:2022-CU25-GDR2-ubuntu-22.04";
        var builder = new ContainerBuilder()
            .WithImage(_image)
            .WithEnvironment("MSSQL_SA_PASSWORD", _password)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithPortBinding(1433, true);

        if (TestContainerReuse.Enabled)
        {
            builder = builder.WithReuse(true);
        }

        _container = builder.Build();
    }


    public override async Task StartAsync()
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
        var host = _container.IpAddress;
        var tmp =
            $@"Server=localhost,{hostPort};uid={_username};pwd={_password};Initial Catalog=master;TrustServerCertificate=true;Connection Timeout=1";
        await WaitForDbToStart(SqlClientFactory.Instance, tmp, _container);
        await createNewDb(tmp);
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