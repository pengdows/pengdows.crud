using System.Data;
using BenchmarkDotNet.Attributes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using pengdows.crud;
using pengdows.crud.attributes;

namespace CrudBenchmarks;

/// <summary>
/// What the 2.0.6 <c>WITH (HOLDLOCK)</c> on the single-row upsert's MERGE costs on a real SQL Server 2022. Three arms do the
/// same one-row upsert, each opening its own connection from the pool per call:
///   - Pengdows: <c>TableGateway.UpsertAsync</c> (MERGE ... WITH (HOLDLOCK), which makes the insert branch atomic)
///   - Raw_Merge_Holdlock: the same statement written by hand
///   - Raw_Merge_2_0_5: the MERGE without the hint that 2.0.5 generated (not atomic against a concurrent insert of the key)
/// Each arm runs against an existing key (the update branch) and a new key (the insert branch).
/// Classification: EqualFootingThroughput + Methodology (statement shape).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SqlServerUpsertBenchmarks : IDisposable
{
    private const int SeedRows = 1000;

    private IContainer _container = null!;
    private string _connStr = null!;
    private DatabaseContext _context = null!;
    private TableGateway<UpsertRow, int> _gateway = null!;
    private int _existing = 1;
    private int _nextNew = 1_000_000;

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
            $"Server=localhost,{hostPort};Database=benchmark;User Id=sa;Password=Benchmark_P@ss1;TrustServerCertificate=True;";
        await WaitForReadyAsync(masterConnStr);

        await using (var master = new SqlConnection(masterConnStr))
        {
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            create.CommandText = "IF DB_ID('benchmark') IS NULL CREATE DATABASE benchmark";
            await create.ExecuteNonQueryAsync();
        }

        await using (var conn = new SqlConnection(_connStr))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"IF OBJECT_ID('upsert_bench') IS NULL CREATE TABLE upsert_bench (
                id INT NOT NULL PRIMARY KEY, name NVARCHAR(100) NOT NULL, age INT NOT NULL, salary FLOAT NOT NULL);
                INSERT INTO upsert_bench (id, name, age, salary)
                SELECT TOP (" + SeedRows + @") ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 'seed', 30, 1000
                FROM sys.all_objects a CROSS JOIN sys.all_objects b";
            await cmd.ExecuteNonQueryAsync();
        }

        _context = new DatabaseContext(_connStr, SqlClientFactory.Instance, new TypeMapRegistry());
        _gateway = new TableGateway<UpsertRow, int>(_context);

        // The three arms must agree: the same row, upserted by each, ends up with the same values.
        foreach (var arm in new[] { 0, 1, 2 })
        {
            var id = 2_000_000 + arm;
            var row = new UpsertRow { Id = id, Name = "check" + arm, Age = 40 + arm, Salary = 2000 + arm };
            await Run(arm, row);
            row.Name += "-again";
            await Run(arm, row);
            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();
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
        if (_context != null)
        {
            await _context.DisposeAsync();
            _context = null!;
        }

        if (_container != null)
        {
            await _container.DisposeAsync();
            _container = null!;
        }
    }

    public void Dispose() => GlobalCleanup().GetAwaiter().GetResult();

    private ValueTask<int> Run(int arm, UpsertRow row) => arm switch
    {
        0 => new ValueTask<int>(_gateway.UpsertAsync(row).AsTask()),
        1 => RawAsync(row, MergeHoldlockSql),
        _ => RawAsync(row, MergeNoHintSql)
    };

    private const string MergeBody =
        " USING (SELECT @id AS id, @name AS name, @age AS age, @salary AS salary) s ON t.id = s.id " +
        "WHEN MATCHED THEN UPDATE SET name = s.name, age = s.age, salary = s.salary " +
        "WHEN NOT MATCHED THEN INSERT (id, name, age, salary) VALUES (s.id, s.name, s.age, s.salary);";

    private const string MergeHoldlockSql = "MERGE upsert_bench WITH (HOLDLOCK) AS t" + MergeBody;

    private const string MergeNoHintSql = "MERGE upsert_bench AS t" + MergeBody;

    private async ValueTask<int> RawAsync(UpsertRow row, string sql)
    {
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = row.Id });
        cmd.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 100) { Value = row.Name });
        cmd.Parameters.Add(new SqlParameter("@age", SqlDbType.Int) { Value = row.Age });
        cmd.Parameters.Add(new SqlParameter("@salary", SqlDbType.Float) { Value = row.Salary });
        return await cmd.ExecuteNonQueryAsync();
    }

    private UpsertRow ExistingRow() => new() { Id = _existing = _existing % SeedRows + 1, Name = "updated", Age = 31, Salary = 1100 };

    private UpsertRow NewRow() => new() { Id = _nextNew++, Name = "inserted", Age = 32, Salary = 1200 };

    [Benchmark(Baseline = true)] public async Task<int> Update_Pengdows() => await Run(0, ExistingRow());
    [Benchmark] public async Task<int> Update_Raw_Merge_Holdlock() => await Run(1, ExistingRow());
    [Benchmark] public async Task<int> Update_Raw_Merge_2_0_5() => await Run(2, ExistingRow());
    [Benchmark] public async Task<int> Insert_Pengdows() => await Run(0, NewRow());
    [Benchmark] public async Task<int> Insert_Raw_Merge_Holdlock() => await Run(1, NewRow());
    [Benchmark] public async Task<int> Insert_Raw_Merge_2_0_5() => await Run(2, NewRow());

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

    [Table("upsert_bench")]
    public class UpsertRow
    {
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
        [Column("age", DbType.Int32)] public int Age { get; set; }
        [Column("salary", DbType.Double)] public double Salary { get; set; }
    }
}
