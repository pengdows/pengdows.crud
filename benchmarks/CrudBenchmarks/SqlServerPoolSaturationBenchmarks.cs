using System.Data;
using System.Globalization;
using System.Threading;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5, invocationCount: 1)]
public class SqlServerPoolSaturationBenchmarks : IAsyncDisposable
{
    private const string Query = "SELECT 1;";
    private static readonly ILogger Logger = ConsoleLogger.Default;

    private IContainer? _container;
    private string _baseConnStr = string.Empty;
    private DatabaseContext _pengdowsContext = null!;
    private TypeMapRegistry _map = null!;

    [Params(8)] public int PoolSize;
    [Params(2)] public int OvercommitFactor;
    [Params(2000)] public int HoldConnectionMs;
    [Params(1)] public int ConnectionTimeoutSeconds;

    private int _lastFailures;
    private int _lastSuccesses;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithEnvironment("MSSQL_SA_PASSWORD", "YourPassword123!")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithPortBinding(1433, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
            .Build();

        await _container.StartAsync();

        var mappedPort = _container.GetMappedPublicPort(1433);
        var masterConnStr =
            $"Server=localhost,{mappedPort};Database=master;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true;Connection Timeout=60;";

        await WaitForReady(masterConnStr);

        _baseConnStr =
            $"Server=localhost,{mappedPort};Database=master;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true;";

        _map = new TypeMapRegistry();
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = BuildConnStr("Benchmark_Pengdows_PoolSaturation"),
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsContext = new DatabaseContext(cfg, SqlClientFactory.Instance, null, _map);
    }

    [BenchmarkCategory("PoolSaturation")]
    [Benchmark(Description = "Dapper open-per-task at PoolSize*2")]
    public async Task Dapper_OpenPerTask_PoolSizeX2()
    {
        _lastFailures = 0;
        _lastSuccesses = 0;

        var totalOps = PoolSize * OvercommitFactor;
        var connStr = BuildConnStr("Benchmark_Dapper_PoolSaturation");
        var waitQuery = BuildWaitQuery();

        await BenchmarkConcurrency.RunConcurrentWithErrors(
            totalOps,
            totalOps,
            async () =>
            {
                await using var connection = new SqlConnection(connStr);
                await connection.OpenAsync();
                await connection.ExecuteScalarAsync<int>(waitQuery);

                Interlocked.Increment(ref _lastSuccesses);
            },
            _ => Interlocked.Increment(ref _lastFailures));

        Logger.WriteLine(
            $"[PoolSaturation] Dapper_OpenPerTask_PoolSizeX2 => Success={_lastSuccesses}, Failures={_lastFailures}, PoolSize={PoolSize}, Overcommit={OvercommitFactor}, HoldMs={HoldConnectionMs}, TimeoutSeconds={ConnectionTimeoutSeconds}");
    }

    [BenchmarkCategory("PoolSaturation")]
    [Benchmark(Description = "pengdows open-per-task at PoolSize*2")]
    public async Task Pengdows_OpenPerTask_PoolSizeX2()
    {
        _lastFailures = 0;
        _lastSuccesses = 0;

        var totalOps = PoolSize * OvercommitFactor;
        var waitQuery = BuildWaitQuery();

        await BenchmarkConcurrency.RunConcurrentWithErrors(
            totalOps,
            totalOps,
            async () =>
            {
                await using var container = _pengdowsContext.CreateSqlContainer(waitQuery);
                await container.ExecuteScalarAsync<int>(CommandType.Text);
                Interlocked.Increment(ref _lastSuccesses);
            },
            _ => Interlocked.Increment(ref _lastFailures));

        Logger.WriteLine(
            $"[PoolSaturation] Pengdows_OpenPerTask_PoolSizeX2 => Success={_lastSuccesses}, Failures={_lastFailures}, PoolSize={PoolSize}, Overcommit={OvercommitFactor}, HoldMs={HoldConnectionMs}, TimeoutSeconds={ConnectionTimeoutSeconds}, OpenConnections={_pengdowsContext.NumberOfOpenConnections}, MaxOpenConnections={_pengdowsContext.MaxNumberOfConnections}, TotalCreated={_pengdowsContext.TotalConnectionsCreated}, TotalReused={_pengdowsContext.TotalConnectionsReused}, TimeoutFailures={_pengdowsContext.TotalConnectionTimeoutFailures}");
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_pengdowsContext is IAsyncDisposable disposableContext)
        {
            await disposableContext.DisposeAsync();
        }

        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await GlobalCleanup();
    }

    private string BuildConnStr(string applicationName)
        => $"{_baseConnStr}Application Name={applicationName};Max Pool Size={PoolSize};Connection Timeout={ConnectionTimeoutSeconds};";

    private string BuildWaitQuery()
    {
        if (HoldConnectionMs <= 0)
        {
            return Query;
        }

        var delay = TimeSpan.FromMilliseconds(HoldConnectionMs)
            .ToString("hh\\:mm\\:ss\\.fff", CultureInfo.InvariantCulture);
        return $"WAITFOR DELAY '{delay}'; {Query}";
    }

    private static async Task WaitForReady(string connectionString)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                await connection.ExecuteScalarAsync<int>(Query);
                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new InvalidOperationException("SQL Server did not become ready in time.");
    }
}
