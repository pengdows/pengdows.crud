using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Hydration-only proof benchmark.
///
/// The goal is to normalize connection setup out of the measured path so the
/// numbers reflect row materialization rather than connection lifecycle policy.
/// Dapper keeps one open SqliteConnection for the duration of the run.
/// pengdows.crud uses DbMode.SingleConnection so the connection is opened once
/// in GlobalSetup and stays open across all iterations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class HydrationHotPathBenchmarks : IDisposable
{
    private const int SeedRows = 5000;

    private string _databasePath = null!;
    private string _connectionString = null!;
    private SqliteConnection _setupConnection = null!;
    private SqliteConnection _dapperConnection = null!;
    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<HydrationBenchEntity, int> _gateway = null!;
    private ISqlContainer _hydrationSc = null!;
    private string _dapperSql = null!;

    [Params(100, 1000, 5000)] public int RowCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"hydration-proof-{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_databasePath};Pooling=false";

        _setupConnection = new SqliteConnection(_connectionString);
        await _setupConnection.OpenAsync();

        await using (var cmd = _setupConnection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE hydration_benchmark (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    email TEXT NOT NULL,
                    age INTEGER NOT NULL,
                    salary REAL NOT NULL,
                    is_active INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    score REAL NOT NULL
                )
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var tx = _setupConnection.BeginTransaction())
        await using (var cmd = _setupConnection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO hydration_benchmark
                    (id, name, email, age, salary, is_active, created_at, score)
                VALUES
                    (@id, @name, @email, @age, @salary, @is_active, @created_at, @score)
                """;

            var id = cmd.CreateParameter();
            id.ParameterName = "@id";
            cmd.Parameters.Add(id);

            var name = cmd.CreateParameter();
            name.ParameterName = "@name";
            cmd.Parameters.Add(name);

            var email = cmd.CreateParameter();
            email.ParameterName = "@email";
            cmd.Parameters.Add(email);

            var age = cmd.CreateParameter();
            age.ParameterName = "@age";
            cmd.Parameters.Add(age);

            var salary = cmd.CreateParameter();
            salary.ParameterName = "@salary";
            cmd.Parameters.Add(salary);

            var isActive = cmd.CreateParameter();
            isActive.ParameterName = "@is_active";
            cmd.Parameters.Add(isActive);

            var createdAt = cmd.CreateParameter();
            createdAt.ParameterName = "@created_at";
            cmd.Parameters.Add(createdAt);

            var score = cmd.CreateParameter();
            score.ParameterName = "@score";
            cmd.Parameters.Add(score);

            for (var i = 1; i <= SeedRows; i++)
            {
                id.Value = i;
                name.Value = $"Entity {i}";
                email.Value = $"entity{i}@example.com";
                age.Value = 20 + (i % 50);
                salary.Value = 50_000d + (i * 10d);
                isActive.Value = i % 2 == 0;
                createdAt.Value = DateTime.UtcNow.AddMinutes(-i).ToString("O");
                score.Value = i * 1.25d;
                await cmd.ExecuteNonQueryAsync();
            }

            tx.Commit();
        }

        _dapperConnection = new SqliteConnection(_connectionString);
        _dapperConnection.Open();

        var typeMap = new TypeMapRegistry();
        typeMap.Register<HydrationBenchEntity>();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = _connectionString,
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        _pengdowsContext = new DatabaseContext(config, SqliteFactory.Instance, null, typeMap);
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
            BenchmarkMetricsWriter.Write(nameof(HydrationHotPathBenchmarks), _pengdowsContext, $"RowCount={RowCount}");
        }

        _hydrationSc?.Dispose();
        _pengdowsContext?.Dispose();
        _dapperConnection?.Dispose();
        _setupConnection?.Dispose();

        if (!string.IsNullOrWhiteSpace(_databasePath) && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
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
                LIMIT {rowCount}
                """;
    }

    [Table("hydration_benchmark")]
    public class HydrationBenchEntity
    {
        [Id][Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
        [Column("email", DbType.String)] public string Email { get; set; } = string.Empty;
        [Column("age", DbType.Int32)] public int Age { get; set; }
        [Column("salary", DbType.Double)] public double Salary { get; set; }
        [Column("is_active", DbType.Boolean)] public bool IsActive { get; set; }
        [Column("created_at", DbType.String)] public string CreatedAt { get; set; } = string.Empty;
        [Column("score", DbType.Double)] public double Score { get; set; }
    }
}
