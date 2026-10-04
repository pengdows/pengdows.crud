using System.Data;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.attributes;

namespace CrudBenchmarks;

/// <summary>
/// DEC-014 / PERF-016: what the pinned connection <c>CreateAsync</c> takes for its generated-id
/// fallback costs. <see cref="CreateAsync"/> is the gateway call (INSERT ... RETURNING on a pinned
/// connection); <see cref="ReturningWithoutLease"/> runs the same statement through the container's
/// own acquire/release and sets the id, which is what CreateAsync would do without the lease.
/// File-backed SQLite (Microsoft.Data.Sqlite), so the connection really opens and returns to the pool.
/// </summary>
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 15)]
public class CreateIdLeaseBenchmarks
{
    private string _path = null!;
    private DatabaseContext _context = null!;
    private TableGateway<Row, long> _gateway = null!;

    [Table("lease_rows")]
    public sealed class Row
    {
        [Id(false)] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    [GlobalSetup]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), $"pengdows-lease-{Guid.NewGuid():N}.db");
        _context = new DatabaseContext($"Data Source={_path}", SqliteFactory.Instance);
        _gateway = new TableGateway<Row, long>(_context);
        using var create = _context.CreateSqlContainer(
            "CREATE TABLE lease_rows (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL)");
        create.ExecuteNonQueryAsync().AsTask().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    [Benchmark(Baseline = true)]
    public async Task<long> CreateAsync()
    {
        var row = new Row { Name = "n" };
        await _gateway.CreateAsync(row);
        return row.Id;
    }

    [Benchmark]
    public async Task<long> ReturningWithoutLease()
    {
        var row = new Row { Name = "n" };
        await using var sc = _gateway.BuildCreateWithReturning(row, true);
        row.Id = Convert.ToInt64(await sc.ExecuteScalarOrNullAsync<object>(pengdows.crud.enums.ExecutionType.Write));
        return row.Id;
    }
}
