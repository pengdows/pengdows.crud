using System.Data;
using System.Data.Common;
using System.Threading;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

[OptInBenchmark]
[MemoryDiagnoser]
// Must run in-process: DatabaseContext is a singleton and cannot be shared across processes.
// BenchmarkDotNet's default out-of-process toolchain spawns a subprocess per benchmark case,
// which would create isolated ADO.NET connection pools that cannot coordinate with each other.
[Config(typeof(MySqlConcurrencyConfig))]
public class MySqlDefaultConcurrencyBenchmarks : IAsyncDisposable
{
    public enum MySqlBenchmarkProvider
    {
        MySqlData,
        MySqlConnector
    }

    // Embedded config forces in-process execution with the same iteration shape as the
    // removed [SimpleJob] attribute. The global BenchmarkConfig has no jobs of its own,
    // so this is the only job that runs (no duplication).
    private sealed class MySqlConcurrencyConfig : ManualConfig
    {
        public MySqlConcurrencyConfig()
        {
            AddJob(Job.Default
                .WithToolchain(InProcessNoEmitToolchain.Instance)
                .WithWarmupCount(1)
                .WithIterationCount(3)
                .WithInvocationCount(1)
                .WithUnrollFactor(1));
        }
    }

    private const string MySqlImage = "mysql:8.0";
    private const int SeedRows = 2000;
    private static readonly string RootPassword = GeneratePassword();

    private static string GeneratePassword()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(18);
        return "Bx3@" + Convert.ToBase64String(bytes).Replace("+", "A").Replace("/", "B").Replace("=", "");
    }
    private const string DatabaseName = "benchdb";
    private const string UserName = "root";

    private IContainer? _container;
    private DbProviderFactory _providerFactory = null!;
    private string _connectionString = string.Empty;
    private IDatabaseContext _context = null!;
    private TableGateway<MySqlBenchAccessRow, long> _gateway = null!;

    private long _maxKnownId;
    private long _lastSuccessCount;
    private long _lastErrorCount;
    private long _lastTimeoutCount;
    private int _errorLogCount;
    private string _currentScenario = string.Empty;
    private static readonly TimeSpan PerOperationTimeout = TimeSpan.FromSeconds(5);

    private const int OperationsPerRun = 2000;
    [Params(32, 64, 128, 256)] public int Parallelism;
    [Params(MySqlBenchmarkProvider.MySqlData, MySqlBenchmarkProvider.MySqlConnector)]
    public MySqlBenchmarkProvider Provider;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _providerFactory = Provider switch
        {
            MySqlBenchmarkProvider.MySqlData => MySql.Data.MySqlClient.MySqlClientFactory.Instance,
            MySqlBenchmarkProvider.MySqlConnector => MySqlConnector.MySqlConnectorFactory.Instance,
            _ => throw new InvalidOperationException($"Unsupported MySQL benchmark provider: {Provider}")
        };

        // Scale max_connections to 3x parallelism so the server never becomes the bottleneck.
        // The semaphore caps in-flight operations at Parallelism; the pool is sized to
        // Parallelism + 20 so the semaphore is always the limiting factor, not the pool.
        _container = new ContainerBuilder()
            .WithImage(MySqlImage)
            .WithEnvironment("MYSQL_ROOT_PASSWORD", RootPassword)
            .WithEnvironment("MYSQL_DATABASE", DatabaseName)
            .WithPortBinding(0, 3306)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(3306))
            .WithCommand($"--max_connections={Parallelism * 3}")
            .Build();

        await _container.StartAsync();

        var mappedPort = _container.GetMappedPublicPort(3306);
        // Pool sized to exactly Parallelism: the semaphore is the admission controller,
        // the pool is sized to match it. Every in-flight operation has a guaranteed slot;
        // there is no pool queuing or timeout — only the semaphore provides backpressure.
        _connectionString =
            $"Server=localhost;Port={mappedPort};Database={DatabaseName};User Id={UserName};Password={RootPassword};Max Pool Size={Parallelism};";

        await WaitForReadyAsync();
        await CreateSchemaAsync();
        await SeedDataAsync();

        var map = new TypeMapRegistry();
        map.Register<MySqlBenchAccessRow>();
        _context = new DatabaseContext(_connectionString, _providerFactory, map);
        _gateway = new TableGateway<MySqlBenchAccessRow, long>(_context);
        _maxKnownId = SeedRows;
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is IAsyncDisposable asyncContext)
        {
            await asyncContext.DisposeAsync();
        }
        else if (_context is IDisposable syncContext)
        {
            syncContext.Dispose();
        }

        if (_container is not null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        var attempted = Math.Max(OperationsPerRun, 1);
        var errorRate = (_lastErrorCount * 100.0) / attempted;
        var timeoutRate = (_lastTimeoutCount * 100.0) / attempted;
        Console.WriteLine(
            $"[MYSQL-CONCURRENCY] Provider={Provider}, scenario={_currentScenario}, parallelism={Parallelism}, operations={OperationsPerRun}, success={_lastSuccessCount}, errors={_lastErrorCount}, errorRate={errorRate:F2}%, timeouts={_lastTimeoutCount}, timeoutRate={timeoutRate:F2}%");
    }

    [Benchmark]
    public async Task ReadOnly_Pengdows_MySql()
    {
        _currentScenario = nameof(ReadOnly_Pengdows_MySql);
        await RunScenarioAsync(ExecuteReadAsync);
    }

    [Benchmark]
    public async Task WriteOnly_Pengdows_MySql()
    {
        _currentScenario = nameof(WriteOnly_Pengdows_MySql);
        await RunScenarioAsync(ExecuteWriteAsync);
    }

    [Benchmark]
    public async Task RandomMix_Pengdows_MySql()
    {
        _currentScenario = nameof(RandomMix_Pengdows_MySql);
        await RunScenarioAsync(ExecuteRandomAsync);
    }

    private async Task RunScenarioAsync(Func<CancellationToken, Task> operation)
    {
        _lastSuccessCount = 0;
        _lastErrorCount = 0;
        _lastTimeoutCount = 0;
        _errorLogCount = 0;

        // Real CancellationToken-driven per-operation timeout, not just relying on the
        // provider's own timeout handling — see BenchmarkConcurrency.RunConcurrentWithTimeout's
        // doc comment for why this exists (a 2026-08-27 incident: MySqlData hung badly enough
        // under 128-way parallelism that the WHOLE batch, not any single operation, exceeded
        // BenchmarkDotNet's own in-process watchdog and aborted the entire run).
        await BenchmarkConcurrency.RunConcurrentWithTimeout(
            OperationsPerRun,
            Parallelism,
            PerOperationTimeout,
            async ct =>
            {
                await operation(ct);
                Interlocked.Increment(ref _lastSuccessCount);
            },
            ex =>
            {
                Interlocked.Increment(ref _lastErrorCount);
                LogSampleError(ex);
            },
            () =>
            {
                Interlocked.Increment(ref _lastTimeoutCount);
                LogSampleError(new TimeoutException(
                    $"Operation exceeded the {PerOperationTimeout.TotalSeconds}s per-operation " +
                    $"timeout (Provider={Provider}, Parallelism={Parallelism})"));
            });

        WriteHangEvidenceIfAny();
    }

    private async Task ExecuteReadAsync(CancellationToken ct)
    {
        var maxId = Math.Max(1, Volatile.Read(ref _maxKnownId));
        var id = Random.Shared.NextInt64(1, maxId + 1);
        _ = await _gateway.RetrieveOneAsync(id, cancellationToken: ct);
    }

    private async Task ExecuteWriteAsync(CancellationToken ct)
    {
        await _gateway.CreateAsync(new MySqlBenchAccessRow
        {
            Payload = Random.Shared.Next(1, 1_000_000),
            Workload = "write",
            UpdatedUtc = DateTime.UtcNow
        }, cancellationToken: ct);

        Interlocked.Increment(ref _maxKnownId);
    }

    private async Task ExecuteRandomAsync(CancellationToken ct)
    {
        var roll = Random.Shared.Next(100);
        if (roll < 55)
        {
            await ExecuteReadAsync(ct);
            return;
        }

        if (roll < 85)
        {
            await ExecuteWriteAsync(ct);
            return;
        }

        await ExecuteUpdateAsync(ct);
    }

    private async Task ExecuteUpdateAsync(CancellationToken ct)
    {
        var maxId = Math.Max(1, Volatile.Read(ref _maxKnownId));
        var id = Random.Shared.NextInt64(1, maxId + 1);

        await using var container = _context.CreateSqlContainer();
        var tableName = container.WrapObjectName("bench_access");
        var idColumn = container.WrapObjectName("id");
        var payloadColumn = container.WrapObjectName("payload");
        var updatedColumn = container.WrapObjectName("updated_utc");

        container.Query.Append("UPDATE ");
        container.Query.Append(tableName);
        container.Query.Append(" SET ");
        container.Query.Append(payloadColumn);
        container.Query.Append(" = ");
        var payloadParam = container.AddParameterWithValue("payload", DbType.Int32, Random.Shared.Next(1, 1_000_000));
        container.Query.Append(container.MakeParameterName(payloadParam));
        container.Query.Append(", ");
        container.Query.Append(updatedColumn);
        container.Query.Append(" = ");
        var updatedParam = container.AddParameterWithValue("updatedUtc", DbType.DateTime, DateTime.UtcNow);
        container.Query.Append(container.MakeParameterName(updatedParam));
        container.Query.Append(" WHERE ");
        container.Query.Append(idColumn);
        container.Query.Append(" = ");
        var idParam = container.AddParameterWithValue("id", DbType.Int64, id);
        container.Query.Append(container.MakeParameterName(idParam));

        _ = await container.ExecuteNonQueryAsync(CommandType.Text, ct);
    }

    /// <summary>
    /// Writes a durable artifact when a hang/timeout is observed, so a driver defect like the
    /// 2026-08-27 MySqlData incident is captured as evidence (exception type, provider,
    /// parallelism, driver package version) instead of relying on anyone's memory of having
    /// seen it before — see BenchmarkConcurrency.RunConcurrentWithTimeout's doc comment.
    /// </summary>
    private void WriteHangEvidenceIfAny()
    {
        if (Interlocked.Read(ref _lastTimeoutCount) == 0)
        {
            return;
        }

        try
        {
            var dir = Environment.GetEnvironmentVariable("CRUD_BENCH_ARTIFACTS_DIR")
                ?? Path.Combine("BenchmarkDotNet.Artifacts", "results");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir,
                $"{nameof(MySqlDefaultConcurrencyBenchmarks)}-hang-evidence-{Provider}-{Parallelism}-{Environment.ProcessId}.md");
            var driverVersion = _providerFactory.GetType().Assembly.GetName().Version?.ToString() ?? "unknown";
            var text =
                $"# MySqlDefaultConcurrencyBenchmarks — timeout/hang evidence\n\n" +
                $"Scenario: {_currentScenario}\n" +
                $"Provider: {Provider} (assembly version {driverVersion})\n" +
                $"Parallelism: {Parallelism}\n" +
                $"Per-operation timeout: {PerOperationTimeout.TotalSeconds}s\n" +
                $"Operations attempted: {OperationsPerRun}\n" +
                $"Timed out: {_lastTimeoutCount}\n" +
                $"Other errors: {_lastErrorCount}\n" +
                $"Succeeded: {_lastSuccessCount}\n" +
                $"Captured: {DateTime.UtcNow:O}\n";
            File.WriteAllText(path, text);
            Console.WriteLine($"[MYSQL-CONCURRENCY] Wrote hang evidence: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MYSQL-CONCURRENCY] Failed to write hang evidence: {ex.Message}");
        }
    }

    private async Task WaitForReadyAsync()
    {
        for (var i = 0; i < 60; i++)
        {
            try
            {
                await using var conn = CreateConnection();
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1;";
                _ = await cmd.ExecuteScalarAsync();
                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("MySQL container did not become ready in time.");
    }

    private async Task CreateSchemaAsync()
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();

        await ExecuteSchemaCommandAsync(conn, "DROP TABLE IF EXISTS bench_access;");
        await ExecuteSchemaCommandAsync(conn, """
                                             CREATE TABLE bench_access (
                                                 id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                                                 payload INT NOT NULL,
                                                 workload VARCHAR(32) NOT NULL,
                                                 updated_utc DATETIME(6) NOT NULL
                                             );
                                             """);
        await ExecuteSchemaCommandAsync(conn, "CREATE INDEX ix_bench_access_payload ON bench_access(payload);");
    }

    private static async Task ExecuteSchemaCommandAsync(DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedDataAsync()
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO bench_access (payload, workload, updated_utc) VALUES (@payload, @workload, @updated_utc);";

        var payload = cmd.CreateParameter();
        payload.ParameterName = "@payload";
        payload.DbType = DbType.Int32;
        cmd.Parameters.Add(payload);

        var workload = cmd.CreateParameter();
        workload.ParameterName = "@workload";
        workload.DbType = DbType.String;
        cmd.Parameters.Add(workload);

        var updatedUtc = cmd.CreateParameter();
        updatedUtc.ParameterName = "@updated_utc";
        updatedUtc.DbType = DbType.DateTime;
        cmd.Parameters.Add(updatedUtc);

        for (var i = 1; i <= SeedRows; i++)
        {
            payload.Value = i;
            workload.Value = "seed";
            updatedUtc.Value = DateTime.UtcNow;
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    private void LogSampleError(Exception ex)
    {
        if (Interlocked.Increment(ref _errorLogCount) <= 5)
        {
            Console.WriteLine(
                $"[MYSQL-CONCURRENCY][ERROR] Provider={Provider}, scenario={_currentScenario}, parallelism={Parallelism}, exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private DbConnection CreateConnection()
    {
        var conn = _providerFactory.CreateConnection()
            ?? throw new InvalidOperationException($"Provider {Provider} did not create a connection.");
        conn.ConnectionString = _connectionString;
        return conn;
    }
}

[Table("bench_access")]
public class MySqlBenchAccessRow
{
    [Id(false)]
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [Column("payload", DbType.Int32)]
    public int Payload { get; set; }

    [Column("workload", DbType.String)]
    public string Workload { get; set; } = string.Empty;

    [Column("updated_utc", DbType.DateTime)]
    public DateTime UpdatedUtc { get; set; }
}
