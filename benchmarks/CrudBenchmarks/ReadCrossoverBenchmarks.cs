using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Where row hydration pays for the per-call cost. One query per call, with the row count swept from 1 to 100,000, on a
/// shared in-memory SQLite database. Each call opens its own connection on both the Dapper and the raw arms, as the
/// default pengdows Standard path does.
/// <list type="bullet">
/// <item>Pengdows: a prebuilt <c>ReadList</c> through the gateway (checks and conversions included).</item>
/// <item>Dapper: the same SQL, a connection per call.</item>
/// <item>Raw: the same SQL, a connection per call, mapped by hand with typed reader calls and no checks. It is the
/// floor; pengdows minus raw is the cost of the mapping and checks, plus pengdows' governance.</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ReadCrossoverBenchmarks : IDisposable
{
    private const string ConnStr = "Data Source=read_crossover_shared;Mode=Memory;Cache=Shared";
    private const string PragmaConnStr = ConnStr + ";Foreign Keys=True";
    private const int SeedRows = 100_000;
    private const string Sql =
        "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > @Age LIMIT @Limit";

    [Params(1, 10, 100, 1000, 10000, 100000)] public int RowCount { get; set; }

    private bool _originalMatchNamesWithUnderscores;
    private SqliteConnection _sentinel = null!;
    private DatabaseContext _context = null!;
    private TableGateway<EqualFootingCrudBenchmarks.BenchEntity, int> _gateway = null!;
    private ISqlContainer _readList = null!;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _originalMatchNamesWithUnderscores = DefaultTypeMap.MatchNamesWithUnderscores;
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        // The sentinel is opened before the seeding connection closes: a shared in-memory database is dropped when its
        // last connection closes.
        _sentinel = new SqliteConnection(ConnStr);
        _sentinel.Open();

        using (var setup = new SqliteConnection(ConnStr))
        {
            setup.Open();
            using (var cmd = setup.CreateCommand())
            {
                cmd.CommandText = @"
                    DROP TABLE IF EXISTS benchmark;
                    CREATE TABLE benchmark (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        name TEXT NOT NULL,
                        age INTEGER NOT NULL,
                        salary REAL NOT NULL,
                        is_active INTEGER NOT NULL,
                        created_at TEXT NOT NULL
                    )";
                cmd.ExecuteNonQuery();
            }

            using var tx = setup.BeginTransaction();
            using var insert = setup.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText =
                "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES ($name, $age, $salary, $active, $created)";
            var name = insert.CreateParameter();
            name.ParameterName = "$name";
            insert.Parameters.Add(name);
            var age = insert.CreateParameter();
            age.ParameterName = "$age";
            insert.Parameters.Add(age);
            var salary = insert.CreateParameter();
            salary.ParameterName = "$salary";
            insert.Parameters.Add(salary);
            var active = insert.CreateParameter();
            active.ParameterName = "$active";
            insert.Parameters.Add(active);
            var created = insert.CreateParameter();
            created.ParameterName = "$created";
            insert.Parameters.Add(created);
            for (var i = 1; i <= SeedRows; i++)
            {
                name.Value = $"Person {i}";
                age.Value = 20 + (i % 50);
                salary.Value = 30000.0 + (i * 100.0);
                active.Value = i % 2;
                created.Value = "2026-01-01T00:00:00.0000000Z";
                insert.ExecuteNonQuery();
            }

            tx.Commit();
        }

        var typeMap = new TypeMapRegistry();
        typeMap.Register<EqualFootingCrudBenchmarks.BenchEntity>();
        _context = new DatabaseContext(ConnStr, SqliteFactory.Instance, typeMap);
        _gateway = new TableGateway<EqualFootingCrudBenchmarks.BenchEntity, int>(_context);

        _readList = _context.CreateSqlContainer();
        _readList.Query.Append(
            "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > ");
        _readList.Query.Append(_readList.MakeParameterName("Age"));
        _readList.Query.Append(" LIMIT ");
        _readList.Query.Append(_readList.MakeParameterName("Limit"));
        _readList.AddParameterWithValue("Age", DbType.Int32, 30);
        _readList.AddParameterWithValue("Limit", DbType.Int32, RowCount);
    }

    [Benchmark(Baseline = true)]
    public async Task<List<EqualFootingCrudBenchmarks.BenchEntity>> Pengdows()
    {
        return await _gateway.LoadListAsync(_readList);
    }

    [Benchmark]
    public async Task<List<EqualFootingCrudBenchmarks.DapperBenchEntity>> Dapper()
    {
        await using var connection = new SqliteConnection(PragmaConnStr);
        await connection.OpenAsync();
        var rows = await connection.QueryAsync<EqualFootingCrudBenchmarks.DapperBenchEntity>(
            Sql, new { Age = 30, Limit = RowCount });
        return rows.AsList();
    }

    [Benchmark]
    public async Task<List<EqualFootingCrudBenchmarks.DapperBenchEntity>> Raw()
    {
        await using var connection = new SqliteConnection(PragmaConnStr);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = Sql;
        command.Parameters.AddWithValue("@Age", 30);
        command.Parameters.AddWithValue("@Limit", RowCount);
        var rows = new List<EqualFootingCrudBenchmarks.DapperBenchEntity>(RowCount);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new EqualFootingCrudBenchmarks.DapperBenchEntity
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Age = reader.GetInt32(2),
                Salary = reader.GetDouble(3),
                IsActive = reader.GetInt64(4) != 0,
                CreatedAt = reader.GetString(5),
            });
        }

        return rows;
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _readList?.Dispose();
        _context?.Dispose();
        _sentinel?.Dispose();
        DefaultTypeMap.MatchNamesWithUnderscores = _originalMatchNamesWithUnderscores;
    }

    public void Dispose() => GlobalCleanup();
}
