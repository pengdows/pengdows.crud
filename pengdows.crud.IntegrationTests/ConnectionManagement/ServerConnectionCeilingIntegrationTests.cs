using System.Collections.Concurrent;
using System.Data;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ConnectionManagement;

/// <summary>
/// A server has one connection ceiling; pengdows.crud runs reads and writes on separate ADO.NET
/// pools. With each role configured at 20 and the server capped at 25, a read burst followed by a
/// write burst leaves the reader pool's idle physical connections open while the writer pool opens
/// its own, so physical connections can reach 40 and the server rejects the 26th — even though the
/// governors only ever count leases in use. These tests state the required behavior: a context
/// must never be able to open more physical connections than the server allows, and callers over
/// the ceiling must wait rather than fail.
/// </summary>
public class ServerConnectionCeilingIntegrationTests : IAsyncLifetime
{
    private const int ServerMaxConnections = 25;
    private const int RolePoolSize = 20;
    private const string Password = "ceiling_test_pw";

    private readonly ITestOutputHelper _output;
    private IContainer? _container;
    private string _connectionString = string.Empty;
    private string _monitorConnectionString = string.Empty;

    public ServerConnectionCeilingIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder()
                .WithImage("postgres:15-alpine")
                .WithEnvironment("POSTGRES_PASSWORD", Password)
                .WithEnvironment("POSTGRES_DB", "ceiling")
                .WithPortBinding(0, 5432)
                .WithCommand("-c", $"max_connections={ServerMaxConnections}")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
                .Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Docker/PostgreSQL unavailable: {ex.Message}");
            _container = null;
            return;
        }

        var port = _container.GetMappedPublicPort(5432);
        _connectionString = $"Host=localhost;Port={port};Database=ceiling;Username=postgres;Password={Password}";
        // The monitor must not be pooled and takes one of the server's slots for itself.
        _monitorConnectionString = _connectionString + ";Pooling=false";

        for (var i = 0; i < 60; i++)
        {
            try
            {
                await using var probe = new NpgsqlConnection(_monitorConnectionString);
                await probe.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        throw new TimeoutException("PostgreSQL container did not become ready.");
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task ReadBurstThenWriteBurst_NeverExceedsTheServersConnectionCeiling()
    {
        Skip.If(_container == null, "Docker/PostgreSQL container is not available.");

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = _connectionString,
            ProviderName = "Npgsql",
            DbMode = DbMode.Standard,
            MaxConcurrentReads = RolePoolSize,
            MaxConcurrentWrites = RolePoolSize,
            EnableMetrics = true,
            ClampPoolsToServerConnectionLimit = true
        };

        using var context = new DatabaseContext(config, NpgsqlFactory.Instance);

        await using var monitor = new NpgsqlConnection(_monitorConnectionString);
        await monitor.OpenAsync();

        var failures = new ConcurrentBag<string>();
        var peakPhysical = 0;

        async Task<int> PhysicalConnectionsAsync()
        {
            await using var cmd = monitor.CreateCommand();
            // Every backend on this database except the monitor itself.
            cmd.CommandText =
                "SELECT count(*) FROM pg_stat_activity WHERE datname = 'ceiling' AND pid <> pg_backend_pid()";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        async Task BurstAsync(ExecutionType type)
        {
            using var sampling = new CancellationTokenSource();
            var sampler = Task.Run(async () =>
            {
                while (!sampling.IsCancellationRequested)
                {
                    var n = await PhysicalConnectionsAsync();
                    InterlockedMax(ref peakPhysical, n);
                    await Task.Delay(50);
                }
            });

            await Parallel.ForEachAsync(
                Enumerable.Range(0, RolePoolSize),
                new ParallelOptions { MaxDegreeOfParallelism = RolePoolSize },
                async (_, ct) =>
                {
                    try
                    {
                        await using var sc = context.CreateSqlContainer("SELECT pg_sleep(1.0)");
                        await sc.ExecuteScalarOrNullAsync<object>(type, CommandType.Text, ct);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{type}: {ex.GetType().Name}: {ex.Message}");
                    }
                });

            sampling.Cancel();
            await sampler;
        }

        await BurstAsync(ExecutionType.Read);
        var afterReads = await PhysicalConnectionsAsync();
        await BurstAsync(ExecutionType.Write);

        _output.WriteLine(
            $"server max_connections={ServerMaxConnections}, role pools={RolePoolSize}/{RolePoolSize}; " +
            $"physical connections after read burst={afterReads}, peak during run={peakPhysical}, " +
            $"failures={failures.Count}");
        foreach (var f in failures.Take(3))
        {
            _output.WriteLine("  " + f);
        }

        Assert.Empty(failures);
        // The monitor holds one slot of its own, so the context may use ServerMaxConnections - 1.
        Assert.True(peakPhysical <= ServerMaxConnections - 1,
            $"peak physical connections {peakPhysical} exceeded the server ceiling {ServerMaxConnections - 1}.");
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
