using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using pengdows.crud;
using pengdows.crud.attributes;

namespace CrudBenchmarks;

/// <summary>
/// What the 2.0.6 change of a single-row upsert from MERGE to INSERT ... ON CONFLICT costs (or saves) on a real PostgreSQL 16.
/// Three arms do the same one-row upsert, each opening its own connection from the pool per call:
///   - Pengdows: <c>TableGateway.UpsertAsync</c> (now INSERT ... ON CONFLICT DO UPDATE)
///   - Raw_OnConflict: the same statement written by hand
///   - Raw_Merge_2_0_5: the MERGE that 2.0.5 generated for PostgreSQL 15+, written by hand
/// Each arm runs against an existing key (the update branch) and a new key (the insert branch).
/// Classification: EqualFootingThroughput + Methodology (statement shape).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class PostgreSqlUpsertBenchmarks : IDisposable
{
    private const int SeedRows = 1000;

    private IContainer _container = null!;
    private string _connStr = null!;
    private DbDataSource _dataSource = null!;
    private DatabaseContext _context = null!;
    private TableGateway<UpsertRow, int> _gateway = null!;
    private int _existing = 1;
    private int _nextNew = 1_000_000;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new ContainerBuilder()
            .WithImage("postgres:16-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "bench")
            .WithEnvironment("POSTGRES_USER", "bench")
            .WithEnvironment("POSTGRES_DB", "benchmark")
            .WithPortBinding(BenchmarkHostPort.ReserveBelowEphemeralRange(), 5432)
            .Build();
        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(5432);
        _connStr = $"Host=localhost;Port={hostPort};Username=bench;Password=bench;Database=benchmark;" +
                   "Pooling=true;Minimum Pool Size=5;Maximum Pool Size=100;Timeout=15;CommandTimeout=30;";
        await WaitForReadyAsync(_connStr);

        _dataSource = new NpgsqlDataSourceBuilder(_connStr + "MaxAutoPrepare=64;AutoPrepareMinUsages=2;").Build();
        await using (var conn = await _dataSource.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS upsert_bench (
                id INTEGER PRIMARY KEY, name TEXT NOT NULL, age INTEGER NOT NULL, salary DOUBLE PRECISION NOT NULL);
                INSERT INTO upsert_bench (id, name, age, salary)
                SELECT g, 'seed', 30, 1000 FROM generate_series(1, " + SeedRows + ") g ON CONFLICT DO NOTHING";
            await cmd.ExecuteNonQueryAsync();
        }

        _context = new DatabaseContext(_connStr, NpgsqlFactory.Instance, new TypeMapRegistry());
        _gateway = new TableGateway<UpsertRow, int>(_context);

        // The three arms must agree: the same row, upserted by each, ends up with the same values.
        foreach (var arm in new[] { 0, 1, 2 })
        {
            var id = 2_000_000 + arm;
            var row = new UpsertRow { Id = id, Name = "check" + arm, Age = 40 + arm, Salary = 2000 + arm };
            await Run(arm, row);
            row.Name += "-again";
            await Run(arm, row);
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT name, age, salary FROM upsert_bench WHERE id = {id}";
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync() || reader.GetString(0) != row.Name || reader.GetInt32(1) != row.Age)
            {
                throw new InvalidOperationException($"upsert arm {arm} produced a different row");
            }
        }
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_context != null) { await _context.DisposeAsync(); _context = null!; }
        if (_dataSource != null) { await _dataSource.DisposeAsync(); _dataSource = null!; }
        if (_container != null) { await _container.DisposeAsync(); _container = null!; }
    }

    public void Dispose() => GlobalCleanup().GetAwaiter().GetResult();

    private ValueTask<int> Run(int arm, UpsertRow row) => arm switch
    {
        0 => new ValueTask<int>(_gateway.UpsertAsync(row).AsTask()),
        1 => RawAsync(row, OnConflictSql),
        _ => RawAsync(row, MergeSql)
    };

    private const string OnConflictSql =
        "INSERT INTO upsert_bench (id, name, age, salary) VALUES (@id, @name, @age, @salary) " +
        "ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, age = EXCLUDED.age, salary = EXCLUDED.salary";

    private const string MergeSql =
        "MERGE INTO upsert_bench t USING (SELECT @id::int AS id, @name::text AS name, @age::int AS age, @salary::float8 AS salary) s " +
        "ON t.id = s.id WHEN MATCHED THEN UPDATE SET name = s.name, age = s.age, salary = s.salary " +
        "WHEN NOT MATCHED THEN INSERT (id, name, age, salary) VALUES (s.id, s.name, s.age, s.salary)";

    private async ValueTask<int> RawAsync(UpsertRow row, string sql)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("id", row.Id));
        cmd.Parameters.Add(new NpgsqlParameter("name", row.Name));
        cmd.Parameters.Add(new NpgsqlParameter("age", row.Age));
        cmd.Parameters.Add(new NpgsqlParameter("salary", row.Salary));
        return await cmd.ExecuteNonQueryAsync();
    }

    private UpsertRow ExistingRow() => new() { Id = _existing = _existing % SeedRows + 1, Name = "updated", Age = 31, Salary = 1100 };

    private UpsertRow NewRow() => new() { Id = _nextNew++, Name = "inserted", Age = 32, Salary = 1200 };

    [Benchmark(Baseline = true)] public async Task<int> Update_Pengdows() => await Run(0, ExistingRow());
    [Benchmark] public async Task<int> Update_Raw_OnConflict() => await Run(1, ExistingRow());
    [Benchmark] public async Task<int> Update_Raw_Merge_2_0_5() => await Run(2, ExistingRow());
    [Benchmark] public async Task<int> Insert_Pengdows() => await Run(0, NewRow());
    [Benchmark] public async Task<int> Insert_Raw_OnConflict() => await Run(1, NewRow());
    [Benchmark] public async Task<int> Insert_Raw_Merge_2_0_5() => await Run(2, NewRow());

    private static async Task WaitForReadyAsync(string connStr)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var c = new NpgsqlConnection(connStr);
                await c.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("PostgreSQL container did not become ready within 30 seconds.");
    }

    [Table("upsert_bench")]
    public class UpsertRow
    {
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
        [Column("age", DbType.Int32)] public int Age { get; set; }
        [Column("salary", DbType.Double)] public double Salary { get; set; }
    }
}
