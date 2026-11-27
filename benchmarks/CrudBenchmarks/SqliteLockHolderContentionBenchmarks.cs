using System.Collections.Concurrent;
using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Write contention against a file-based SQLite database with a deterministic lock holder, and no pragmas.
///
/// A separate holder connection takes the database write lock (BEGIN IMMEDIATE) and keeps it for
/// <see cref="HoldMilliseconds"/>. Then <see cref="Contenders"/> concurrent transactions from each framework try
/// to write. The only knob is the command timeout, <see cref="TimeoutSeconds"/>, written into the connection
/// string that all three frameworks share, so every arm waits for the lock under the same rule.
///
/// Expected shape: with a timeout shorter than the hold, the uncontrolled arms fail; with a timeout longer
/// than the hold, they wait and succeed. Pengdows's SingleWriter gate admits one of its own writers at a time,
/// so its contenders queue inside the process rather than colliding with each other. The holder is outside
/// the gate for every arm, so it is a fair common blocker.
///
/// Primary result: lost transactions (attempted minus committed), printed per run.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 0, iterationCount: 5, invocationCount: 1)]
public class SqliteLockHolderContentionBenchmarks : IDisposable
{
    private const int HoldMilliseconds = 2000;
    private const int Contenders = 50;
    private const int WritesPerTransaction = 10;
    private const int Rows = 100;

    [Params(0, 1, 5)]
    public int TimeoutSeconds { get; set; }

    private string _directory = string.Empty;
    private string _connectionString = string.Empty;
    private DatabaseContext _pengdows = null!;
    private DbContextOptions<ContentionEfContext> _efOptions = null!;
    private readonly ConcurrentDictionary<string, int> _failures = new();
    private int _attempted;
    private int _committed;

    [GlobalSetup]
    public async Task Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pengdows-lockholder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "contention.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            DefaultTimeout = TimeoutSeconds
        }.ToString();

        await using (var conn = new SqliteConnection(_connectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("CREATE TABLE contention (id INTEGER PRIMARY KEY, value INTEGER NOT NULL)");
            for (var i = 1; i <= Rows; i++)
            {
                await conn.ExecuteAsync("INSERT INTO contention (id, value) VALUES (@id, 0)", new { id = i });
            }
        }

        var typeMap = new TypeMapRegistry();
        typeMap.Register<ContentionRow>();
        _pengdows = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = _connectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            EnableMetrics = true
        }, Microsoft.Data.Sqlite.SqliteFactory.Instance, null, typeMap);

        _efOptions = new DbContextOptionsBuilder<ContentionEfContext>().UseSqlite(_connectionString).Options;
    }

    [IterationSetup]
    public void ResetCounters()
    {
        _failures.Clear();
        Interlocked.Exchange(ref _attempted, 0);
        Interlocked.Exchange(ref _committed, 0);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pengdows?.Dispose();
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // The directory name is unique per run; a leftover file does not affect any other run.
        }
    }

    public void Dispose() => Cleanup();

    [Benchmark(Baseline = true)]
    public Task Contention_Dapper() => RunContention(Dapper_Transaction, "Dapper");

    [Benchmark]
    public Task Contention_EntityFramework() => RunContention(EntityFramework_Transaction, "EntityFramework");

    [Benchmark]
    public Task Contention_Pengdows() => RunContention(Pengdows_Transaction, "Pengdows");

    private async Task RunContention(Func<int, Task> transaction, string framework)
    {
        // The holder takes the lock first; contenders start only after it has it.
        var poolBefore = _pengdows.Metrics;
        var holder = HoldLockAsync();
        await Task.Delay(200);

        var tasks = new Task[Contenders];
        for (var t = 0; t < Contenders; t++)
        {
            var index = t;
            tasks[t] = Task.Run(async () =>
            {
                Interlocked.Increment(ref _attempted);
                try
                {
                    await transaction(index);
                    Interlocked.Increment(ref _committed);
                }
                catch (Exception ex)
                {
                    _failures.AddOrUpdate(framework + ": " + ex.GetType().Name + " | " + ((ex.InnerException ?? ex).Message), 1, (_, n) => n + 1);
                }
            });
        }

        await Task.WhenAll(tasks);
        await holder;
        PrintSummary(framework);
        if (framework == "Pengdows")
        {
            var poolAfter = _pengdows.Metrics;
            Console.WriteLine($"[SqliteLockHolderContention] timeout={TimeoutSeconds}s Pengdows metrics: " +
                              $"writeRequests+={poolAfter.WriteRequests - poolBefore.WriteRequests} " +
                              $"writePoolWaits+={poolAfter.WritePoolWaits - poolBefore.WritePoolWaits} " +
                              $"writePoolTimeouts+={poolAfter.WritePoolTimeouts - poolBefore.WritePoolTimeouts}");
        }
    }

    private async Task HoldLockAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        await conn.ExecuteAsync("UPDATE contention SET value = value + 1 WHERE id = 1", transaction: tx);
        await Task.Delay(HoldMilliseconds);
        tx.Commit();
    }

    private void PrintSummary(string framework)
    {
        var attempted = Volatile.Read(ref _attempted);
        var committed = Volatile.Read(ref _committed);
        Console.WriteLine($"[SqliteLockHolderContention] timeout={TimeoutSeconds}s {framework}: " +
                          $"attempted={attempted} committed={committed} lost={attempted - committed}");
        foreach (var (key, count) in _failures.OrderByDescending(pair => pair.Value))
        {
            Console.WriteLine($"[SqliteLockHolderContention]   {key} x{count}");
        }
    }

    private async Task Dapper_Transaction(int index)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();
        for (var j = 0; j < WritesPerTransaction; j++)
        {
            await conn.ExecuteAsync(
                "UPDATE contention SET value = @value WHERE id = @id",
                new { value = index * 1000 + j, id = (j + index) % Rows + 1 },
                tx);
        }

        tx.Commit();
    }

    private async Task EntityFramework_Transaction(int index)
    {
        await using var ef = new ContentionEfContext(_efOptions);
        await using var tx = await ef.Database.BeginTransactionAsync();
        for (var j = 0; j < WritesPerTransaction; j++)
        {
            await ef.Database.ExecuteSqlRawAsync(
                "UPDATE contention SET value = {0} WHERE id = {1}",
                index * 1000 + j,
                (j + index) % Rows + 1);
        }

        await tx.CommitAsync();
    }

    private async Task Pengdows_Transaction(int index)
    {
        await using var tx = await _pengdows.BeginTransactionAsync();
        for (var j = 0; j < WritesPerTransaction; j++)
        {
            await using var update = tx.CreateSqlContainer();
            update.Query.Append("UPDATE contention SET value = ");
            update.Query.Append(update.MakeParameterName("value"));
            update.Query.Append(" WHERE id = ");
            update.Query.Append(update.MakeParameterName("id"));
            update.AddParameterWithValue("value", DbType.Int32, index * 1000 + j);
            update.AddParameterWithValue("id", DbType.Int32, (j + index) % Rows + 1);
            await update.ExecuteNonQueryAsync(ExecutionType.Write);
        }

        await tx.CommitAsync();
    }

    [Table("contention")]
    public class ContentionRow
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("value", DbType.Int32)] public int Value { get; set; }
    }

    public class ContentionEfContext : DbContext
    {
        public ContentionEfContext(DbContextOptions<ContentionEfContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ContentionEfRow>(entity =>
            {
                entity.ToTable("contention");
                entity.HasKey(row => row.Id);
            });
        }
    }

    public class ContentionEfRow
    {
        public int Id { get; set; }
        public int Value { get; set; }
    }
}
