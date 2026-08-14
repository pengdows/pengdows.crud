using System.Data;
using System.Text;
using BenchmarkDotNet.Attributes;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// SQL Server hydration-only proof benchmark — the SQL-Server counterpart to
/// HydrationHotPathBenchmarks.cs (SQLite).
///
/// SqlServerEqualFootingBenchmarks.cs uses DbMode.Standard, so pengdows pays a fresh
/// session-settings SET round trip on every single operation (see
/// docs/FUTURE_WORK.md's P2 entry on this). This benchmark asks a different question:
/// once that per-operation session-init tax is paid ONCE instead of once per operation
/// (DbMode.SingleConnection — same normalization HydrationHotPathBenchmarks.cs already
/// applies for SQLite), how does pengdows's actual row-materialization cost compare to
/// Dapper's, which also keeps a single connection open for the whole run?
///
/// Dapper keeps one permanently open SqlConnection for the duration of the run.
/// pengdows.crud uses DbMode.SingleConnection so its connection (and the one-time
/// session-settings SET batch) is established once in GlobalSetup and reused across
/// all iterations, exactly mirroring Dapper's connection-lifecycle policy.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SqlServerHydrationHotPathBenchmarks : IDisposable
{
    private const int SeedRows = 5000;

    private IContainer _container = null!;
    private string _connStr = null!;
    private SqlConnection _dapperConnection = null!;
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<HydrationBenchEntity, int> _gateway = null!;
    private ISqlContainer _hydrationSc = null!;
    private string _dapperSql = null!;

    [Params(100, 1000, 5000)] public int RowCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", "Benchmark_P@ss1")
            .WithEnvironment("MSSQL_PID", "Developer")
            .WithPortBinding(1433, true)
            .Build();

        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(1433);
        var masterConnStr =
            $"Server=localhost,{hostPort};Database=master;User Id=sa;Password=Benchmark_P@ss1;TrustServerCertificate=True;";
        _connStr =
            $"Server=localhost,{hostPort};Database=hydrationbench;User Id=sa;Password=Benchmark_P@ss1;TrustServerCertificate=True;";

        await WaitForReadyAsync(masterConnStr);

        await using (var masterConn = new SqlConnection(masterConnStr))
        {
            await masterConn.OpenAsync();
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = "IF DB_ID('hydrationbench') IS NULL CREATE DATABASE hydrationbench";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var conn = new SqlConnection(_connStr))
        {
            await conn.OpenAsync();

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    IF OBJECT_ID('hydration_benchmark', 'U') IS NULL
                    CREATE TABLE hydration_benchmark (
                        id          INT IDENTITY(1,1) PRIMARY KEY,
                        name        NVARCHAR(255) NOT NULL,
                        email       NVARCHAR(255) NOT NULL,
                        age         INT NOT NULL,
                        salary      FLOAT NOT NULL,
                        is_active   BIT NOT NULL,
                        created_at  NVARCHAR(50) NOT NULL,
                        score       FLOAT NOT NULL
                    )";
                await cmd.ExecuteNonQueryAsync();
            }

            var now = DateTime.UtcNow.ToString("O");
            const int batchSize = 500;
            for (var start = 1; start <= SeedRows; start += batchSize)
            {
                var end = Math.Min(start + batchSize - 1, SeedRows);
                var sb = new StringBuilder(
                    "INSERT INTO hydration_benchmark (name, email, age, salary, is_active, created_at, score) VALUES ");
                for (var i = start; i <= end; i++)
                {
                    if (i > start) sb.Append(',');
                    var active = (i % 2 == 0) ? "1" : "0";
                    sb.Append(
                        $"(N'Entity {i}', N'entity{i}@example.com', {20 + (i % 50)}, {50_000.0 + i * 10.0}, {active}, N'{now}', {i * 1.25})");
                }

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sb.ToString();
                await cmd.ExecuteNonQueryAsync();
            }
        }

        _dapperConnection = new SqlConnection(_connStr);
        await _dapperConnection.OpenAsync();

        var typeMap = new TypeMapRegistry();
        typeMap.Register<HydrationBenchEntity>();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = _connStr,
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        _pengdowsContext = new DatabaseContext(config, SqlClientFactory.Instance, null, typeMap);
        _gateway = new TableGateway<HydrationBenchEntity, int>(_pengdowsContext);

        _dapperSql = BuildSql(RowCount);
        _hydrationSc = _pengdowsContext.CreateSqlContainer(BuildSql(RowCount));

        await PreWarmAsync();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (_pengdowsContext != null)
        {
            BenchmarkMetricsWriter.Write(nameof(SqlServerHydrationHotPathBenchmarks), _pengdowsContext, $"RowCount={RowCount}");
        }

        _hydrationSc?.Dispose();
        _pengdowsContext?.Dispose();
        _dapperConnection?.Dispose();
        _container?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        GlobalCleanup();
    }

    [Benchmark(Baseline = true)]
    public async Task<List<HydrationBenchEntity>> HydrationOnly_Pengdows()
    {
        return await _gateway.LoadListAsync(_hydrationSc);
    }

    [Benchmark]
    public List<HydrationBenchEntity> HydrationOnly_Dapper()
    {
        return _dapperConnection.Query<HydrationBenchEntity>(_dapperSql).AsList();
    }

    private async Task PreWarmAsync()
    {
        for (var i = 0; i < 5; i++)
        {
            await _gateway.LoadListAsync(_hydrationSc);
            _ = _dapperConnection.Query<HydrationBenchEntity>(_dapperSql).AsList();
        }
    }

    private static async Task WaitForReadyAsync(string connStr)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                await using var c = new SqlConnection(connStr);
                await c.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("SQL Server container did not become ready within 60 seconds.");
    }

    private static string BuildSql(int rowCount)
    {
        return $"""
               SELECT
                   id,
                   name,
                   email,
                   age,
                   salary,
                   is_active AS IsActive,
                   created_at AS CreatedAt,
                   score
               FROM hydration_benchmark
               ORDER BY id
               OFFSET 0 ROWS FETCH NEXT {rowCount} ROWS ONLY
               """;
    }

    [Table("hydration_benchmark")]
    public class HydrationBenchEntity
    {
        [Id(false)][Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
        [Column("email", DbType.String)] public string Email { get; set; } = string.Empty;
        [Column("age", DbType.Int32)] public int Age { get; set; }
        [Column("salary", DbType.Double)] public double Salary { get; set; }
        [Column("is_active", DbType.Boolean)] public bool IsActive { get; set; }
        [Column("created_at", DbType.String)] public string CreatedAt { get; set; } = string.Empty;
        [Column("score", DbType.Double)] public double Score { get; set; }
    }
}
