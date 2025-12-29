#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using pengdows.crud;

#endregion

namespace testbed;

public class SqlServerTestContainer : TestContainer
{
    private readonly IContainer _container;
    private string? _connectionString;
    private string _database = "testdb";
    private string _password = "YourPassword123";
    private string _username = "sa";

    //docker run -e 'ACCEPT_EULA=Y' -e 'MSSQL_SA_PASSWORD=YourPassword123' -p 1433:1433 --name sql_server_container -d mcr.microsoft.com/mssql/server

    // var sqlConnectionString =
    //     "Server=localhost;uid=sa;pwd=YourPassword123;Initial Catalog=testdb;TrustServerCertificate=true";
    public SqlServerTestContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/mssql/server")
            .WithEnvironment("MSSQL_SA_PASSWORD", _password)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithPortBinding(1433, true)
            .Build();
    }


    public override async Task StartAsync()
    {
        await _container.StartAsync();
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
        //this gyration parses and scrubs the connectoin string.
        csb.ConnectionString = connectionString;
        connection.ConnectionString = csb.ConnectionString;

        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID('{_database}') IS NULL CREATE DATABASE [{_database}]";
        await command.ExecuteNonQueryAsync();
        csb["Initial Catalog"] = "testdb";
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

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}
