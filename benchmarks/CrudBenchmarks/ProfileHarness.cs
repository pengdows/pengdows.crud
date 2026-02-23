using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Profiling harness for dotnet-trace. Run with:
///   dotnet run -c Release --project benchmarks/CrudBenchmarks -- --profile
/// Then in another terminal:
///   dotnet-trace collect -p {PID} --providers Microsoft-DotNETCore-SampleProfiler
/// </summary>
public static class ProfileHarness
{
    private const string ConnStr = "Data Source=ProfileBench;Mode=Memory;Cache=Shared";
    private const int SeedRows = 1000;
    private const int Iterations = 50_000;

    public static async Task Run()
    {
        using var sentinel = new SqliteConnection(ConnStr);
        sentinel.Open();

        using (var cmd = sentinel.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS benchmark (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    age INTEGER NOT NULL
                )";
            cmd.ExecuteNonQuery();
        }

        using (var tx = sentinel.BeginTransaction())
        {
            using var cmd = sentinel.CreateCommand();
            cmd.Transaction = tx;
            for (var i = 1; i <= SeedRows; i++)
            {
                cmd.CommandText =
                    $"INSERT INTO benchmark (id, name, age) VALUES ({i}, 'Person {i}', {20 + (i % 50)})";
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // Setup pengdows
        var typeMap = new TypeMapRegistry();
        typeMap.Register<ProfileEntity>();
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = ConnStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        using var context = new DatabaseContext(cfg, SqliteFactory.Instance, null, typeMap);
        var gateway = new TableGateway<ProfileEntity, int>(context);
        var pengdowsSql = $"SELECT id, name, age FROM benchmark WHERE id = {context.MakeParameterName("id")}";

        // Setup Dapper
        const string dapperSql = "SELECT id, name, age FROM benchmark WHERE id = @id";

        // Warmup
        for (var i = 0; i < 100; i++)
        {
            await using var sc = context.CreateSqlContainer(pengdowsSql);
            sc.AddParameterWithValue("id", DbType.Int32, (i % SeedRows) + 1);
            await gateway.LoadSingleAsync(sc);
        }

        for (var i = 0; i < 100; i++)
        {
            await using var conn = new SqliteConnection(ConnStr);
            await conn.OpenAsync();
            await conn.QuerySingleOrDefaultAsync<DapperProfileEntity>(dapperSql, new { id = (i % SeedRows) + 1 });
        }

        Console.WriteLine($"PID: {Environment.ProcessId}");
        Console.WriteLine($"Iterations: {Iterations}");

        // === PENGDOWS PHASE ===
        Console.WriteLine("Running pengdows...");
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            await using var sc = context.CreateSqlContainer(pengdowsSql);
            sc.AddParameterWithValue("id", DbType.Int32, (i % SeedRows) + 1);
            await gateway.LoadSingleAsync(sc);
        }

        sw.Stop();
        Console.WriteLine(
            $"Pengdows: {sw.ElapsedMilliseconds}ms total, {sw.Elapsed.TotalMicroseconds / Iterations:F2}us/op");

        // === DAPPER PHASE ===
        Console.WriteLine("Running Dapper...");
        sw.Restart();
        for (var i = 0; i < Iterations; i++)
        {
            await using var conn = new SqliteConnection(ConnStr);
            await conn.OpenAsync();
            await conn.QuerySingleOrDefaultAsync<DapperProfileEntity>(dapperSql, new { id = (i % SeedRows) + 1 });
        }

        sw.Stop();
        Console.WriteLine(
            $"Dapper: {sw.ElapsedMilliseconds}ms total, {sw.Elapsed.TotalMicroseconds / Iterations:F2}us/op");
    }

    [Table("benchmark")]
    public sealed class ProfileEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Column("age", DbType.Int32)] public int Age { get; set; }
    }

    public sealed class DapperProfileEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }
}