#region

using AdoNetCore.AseClient;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud;

#endregion

namespace testbed.Sybase;

public class SybaseTestContainer : TestContainer, ITestContainer
{
    private const string Username = "sa";
    private const string Password = "MyStr0ngP@ssw0rd";
    private const string Database = "testdb";
    private readonly IContainer _container;
    private string? _connectionString;

    public SybaseTestContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("nguoianphu/docker-sybase")
            .WithEnvironment("SA_PASSWORD", Password)
            .WithPortBinding(5000, true)
            .WithPortBinding(5001, true)
            .WithWaitStrategy(Wait.ForUnixContainer())
            .Build();

        // start immediately so we can await in StartAsync
        _container.StartAsync().Wait();
    }

    public override async Task StartAsync()
    {
        var hostPort = _container.GetMappedPublicPort(5000);
        var cs = $"DataSource=localhost;Port={hostPort};Database=master;Uid={Username};Pwd={Password};";

        // wait for ASE to be ready
        await WaitForDbToStart(AseClientFactory.Instance, cs, _container);

        // create test database
        await CreateTestDatabase(cs);

        // switch default catalog
        _connectionString = $"DataSource=localhost;Port={hostPort};Database={Database};Uid={Username};Pwd={Password};";
    }

    private static async Task CreateTestDatabase(string masterCs)
    {
        await using var conn = AseClientFactory.Instance.CreateConnection();
        conn.ConnectionString = masterCs;
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF NOT EXISTS (SELECT name FROM sysdatabases WHERE name = '{Database}') " +
                          $"CREATE DATABASE {Database}";
        await cmd.ExecuteNonQueryAsync();
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
            throw new InvalidOperationException("Container not started.");

        return Task.FromResult<IDatabaseContext>(
            new DatabaseContext(
                _connectionString,
                AseClientFactory.Instance,
                services.GetRequiredService<ITypeMapRegistry>()));
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}