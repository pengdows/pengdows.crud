using BenchmarkDotNet.Attributes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Npgsql;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

[MemoryDiagnoser]
public class PostgreSqlGovernorComparisonBenchmarks : IAsyncDisposable
{
    internal const int ServerMaxConnections = 100;
    internal const int GovernorCapacity = 20;
    internal const int Operations = 5000;
    internal const int Parallelism = 1000;

    private const string Password = "Governor_Compare_P@ss1";
    private IContainer _container = null!;
    private string _connectionString = string.Empty;
    private PoolGovernor _semaphoreGovernor = null!;
    private SemaphoreSlim _semaphoreAdmission = null!;
    private PoolGovernorAdaptiveCopy _adaptiveGovernor = null!;

    internal static string BuildComparisonConnectionString(string connectionString) =>
        $"{connectionString};Pooling=true;Maximum Pool Size=100";

    [GlobalSetup]
    public async Task Setup()
    {
        _container = new ContainerBuilder()
            .WithImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", Password)
            .WithEnvironment("POSTGRES_DB", "gov_compare")
            .WithCommand("-c", $"max_connections={ServerMaxConnections}")
            .WithPortBinding(0, 5432)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();
        await _container.StartAsync();

        var port = _container.GetMappedPublicPort(5432);
        _connectionString = BuildComparisonConnectionString(
            $"Host=localhost;Port={port};Database=gov_compare;Username=postgres;Password={Password}");
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE gov_items (id integer PRIMARY KEY, val integer NOT NULL); INSERT INTO gov_items VALUES (1, 42);";
        await command.ExecuteNonQueryAsync();

        _semaphoreAdmission = new SemaphoreSlim(GovernorCapacity, GovernorCapacity);
        _semaphoreGovernor = new PoolGovernor(PoolLabel.Writer, "pg-governor-compare", GovernorCapacity,
            TimeSpan.FromSeconds(30), maxQueueDepth: Parallelism * 2, sharedSemaphore: _semaphoreAdmission);
        _adaptiveGovernor = new PoolGovernorAdaptiveCopy(PoolLabel.Writer, "pg-governor-compare", GovernorCapacity,
            TimeSpan.FromSeconds(30), maxQueueDepth: Parallelism * 2);
    }

    [Benchmark(Baseline = true)]
    public Task SemaphoreGovernor() => RunAsync(_semaphoreGovernor);

    [Benchmark]
    public Task AdaptiveGovernor() => RunAsync(_adaptiveGovernor);

    private async Task RunAsync(PoolGovernor governor)
    {
        await RunWorkersAsync(async () =>
        {
            await using var slot = await governor.AcquireAsync();
            await ExecuteAsync();
        });
    }

    private async Task RunAsync(PoolGovernorAdaptiveCopy governor)
    {
        await RunWorkersAsync(async () =>
        {
            await using var slot = await governor.AcquireAsync();
            await ExecuteAsync();
        });
    }

    private static Task RunWorkersAsync(Func<Task> operation) =>
        Task.WhenAll(Enumerable.Range(0, Parallelism).Select(async _ =>
        {
            for (var i = 0; i < Operations / Parallelism; i++)
            {
                await operation();
            }
        }));

    private async Task ExecuteAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT val FROM gov_items WHERE id = 1";
        _ = await command.ExecuteScalarAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _semaphoreGovernor?.Dispose();
        _semaphoreAdmission?.Dispose();
        _adaptiveGovernor?.Dispose();
        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    public ValueTask DisposeAsync() => new(Cleanup());
}

[MemoryDiagnoser]
public class SqlServerGovernorComparisonBenchmarks : IAsyncDisposable
{
    internal const string DatabaseName = "gov_test";
    internal const int ServerMaxConnections = 100;
    internal const int GovernorCapacity = 20;
    internal const int Operations = 5000;
    internal const int Parallelism = 1000;

    private const string Password = "Benchmark_P@ss1";
    private IContainer _container = null!;
    private string _connectionString = string.Empty;
    private PoolGovernor _semaphoreGovernor = null!;
    private SemaphoreSlim _semaphoreAdmission = null!;
    private PoolGovernorAdaptiveCopy _adaptiveGovernor = null!;

    internal static string BuildComparisonConnectionString(string connectionString) =>
        $"{connectionString};Pooling=true;Max Pool Size=100";

    [GlobalSetup]
    public async Task Setup()
    {
        int hostPort;
        using (var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
        {
            probe.Start();
            hostPort = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        }

        _container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", Password)
            .WithEnvironment("MSSQL_PID", "Developer")
            .WithPortBinding(hostPort, 1433)
            .Build();
        await _container.StartAsync();

        var master = $"Server=localhost,{hostPort};Database=master;User Id=sa;Password={Password};TrustServerCertificate=True;Pooling=false";
        await WaitForSqlServerAsync(master);
        await using (var connection = new SqlConnection(master))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"EXEC sp_configure 'show advanced options', 1; RECONFIGURE; EXEC sp_configure 'user connections', {ServerMaxConnections}; RECONFIGURE;";
            await command.ExecuteNonQueryAsync();
        }

        await _container.StopAsync();
        await _container.StartAsync();
        await WaitForSqlServerAsync(master);

        await using (var connection = new SqlConnection(master))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{DatabaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        _connectionString = BuildComparisonConnectionString(
            $"Server=localhost,{hostPort};Database={DatabaseName};User Id=sa;Password={Password};TrustServerCertificate=True");
        await using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "IF OBJECT_ID('dbo.gov_items') IS NULL BEGIN CREATE TABLE dbo.gov_items (id INT PRIMARY KEY, val INT NOT NULL); INSERT INTO dbo.gov_items VALUES (1, 42); END";
            await command.ExecuteNonQueryAsync();
        }

        _semaphoreAdmission = new SemaphoreSlim(GovernorCapacity, GovernorCapacity);
        _semaphoreGovernor = new PoolGovernor(PoolLabel.Writer, "sqlserver-governor-compare", GovernorCapacity,
            TimeSpan.FromSeconds(30), maxQueueDepth: Parallelism * 2, sharedSemaphore: _semaphoreAdmission);
        _adaptiveGovernor = new PoolGovernorAdaptiveCopy(PoolLabel.Writer, "sqlserver-governor-compare", GovernorCapacity,
            TimeSpan.FromSeconds(30), maxQueueDepth: Parallelism * 2);
    }

    [Benchmark(Baseline = true)]
    public Task SemaphoreGovernor() => RunAsync(_semaphoreGovernor);

    [Benchmark]
    public Task AdaptiveGovernor() => RunAsync(_adaptiveGovernor);

    private async Task RunAsync(PoolGovernor governor)
    {
        await RunWorkersAsync(async () =>
        {
            await using var slot = await governor.AcquireAsync();
            await ExecuteAsync();
        });
    }

    private async Task RunAsync(PoolGovernorAdaptiveCopy governor)
    {
        await RunWorkersAsync(async () =>
        {
            await using var slot = await governor.AcquireAsync();
            await ExecuteAsync();
        });
    }

    private static Task RunWorkersAsync(Func<Task> operation) =>
        Task.WhenAll(Enumerable.Range(0, Parallelism).Select(async _ =>
        {
            for (var i = 0; i < Operations / Parallelism; i++)
            {
                await operation();
            }
        }));

    private async Task ExecuteAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT val FROM dbo.gov_items WHERE id = 1";
        _ = await command.ExecuteScalarAsync();
    }

    private static async Task WaitForSqlServerAsync(string connectionString)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                return;
            }
            catch when (attempt < 59)
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("SQL Server did not become ready in time.");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _semaphoreGovernor?.Dispose();
        _semaphoreAdmission?.Dispose();
        _adaptiveGovernor?.Dispose();
        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    public ValueTask DisposeAsync() => new(Cleanup());
}
