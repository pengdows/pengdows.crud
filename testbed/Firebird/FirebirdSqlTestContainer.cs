#region

using System.Data.Common;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Extensions.DependencyInjection;
using pengdow.crud;

#endregion

namespace testbed;

public class FirebirdSqlTestContainer : TestContainer
{
    private readonly IContainer _container;
    private readonly DbProviderFactory _factory = FirebirdClientFactory.Instance;
    private string? _connectionString;
    private string _database = "/var/lib/firebird/data/testdb.fdb"; //"/firebird/data/testdb.fdb";
    private string _password = "mysecretpassword";

    private int _port = 3050;
    private string _username = "SYSDBA";

    public FirebirdSqlTestContainer()
    {
        //FIREBIRD_ROOT_PASSWORD
        // 
        // Firebird installer generates a one-off password for SYSDBA and stores it in /opt/firebird/SYSDBA.password.
        // 
        // If FIREBIRD_ROOT_PASSWORD is set, SYSDBA password will be changed. And the file /opt/firebird/SYSDBA.password will be removed.
        // FIREBIRD_USER
        // 
        // Creates an user in Firebird security database.
        // 
        // You must inform a password in FIREBIRD_PASSWORD variable. Otherwise the container initialization will fail.
        // FIREBIRD_DATABASE
        //-u SYSDBA
        _container = new ContainerBuilder()
            .WithImage("firebirdsql/firebird")
            .WithPortBinding(3050, true)
            .WithEnvironment("ISC_PASSWORD", _password)
            .WithEnvironment("FIREBIRD_ROOT_PASSWORD", _password)
            .WithEnvironment("FIREBIRD_DATABASE", _database)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilPortIsAvailable(3050))
            .Build();
    }

    public override async Task StartAsync()
    {
        try
        {
            await _container.StartAsync();
            var hostPort = _container.GetMappedPublicPort(_port);

            _connectionString = new FbConnectionStringBuilder
            {
                DataSource = "localhost",
                Port = hostPort,
                Database = _database,
                UserID = _username,
                Password = _password,
                Charset = "UTF8",
                Pooling = true,
                ConnectionTimeout = 10,
                CommandTimeout = 30
            }.ToString();
            Console.WriteLine($"Connecting to {_connectionString}");

            await WaitForDbToStart(_factory, _connectionString, _container);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    public override async Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
            throw new InvalidOperationException("Container not started yet.");

        return new DatabaseContext(_connectionString, _factory,
            services.GetRequiredService<ITypeMapRegistry>());
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}