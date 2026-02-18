using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3, invocationCount: 1)]
public class SqliteConcurrencyBenchmark
{
    private const string DbFileName = "concurrency_test.db";
    private string _connectionString = $"Data Source={DbFileName};";

    // pengdows.crud context
    private IDatabaseContext _pengdowsContext = null!;
    private TableGateway<TestEntity, int> _pengdowsGateway = null!;
    private int _pengdowsInsertCounter = 0;

    // EF Core options
    private DbContextOptions<EfSqliteDbContext> _efCoreOptions = null!;

    // Dapper connection string
    private string _dapperConnectionString = null!;

    private long _pengdowsSuccessCount;
    private long _pengdowsErrorCount;
    private long _dapperSuccessCount;
    private long _dapperErrorCount;
    private long _efCoreSuccessCount;
    private long _efCoreErrorCount;

    [Params(100)]
    public int Operations;

    [Params(16)]
    public int Parallelism;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Ensure clean state
        if (File.Exists(DbFileName))
        {
            File.Delete(DbFileName);
        }

        // Create and schema the database
        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE TestEntities (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, Counter INTEGER)";
            command.ExecuteNonQuery();
        }

        // 1. Setup pengdows.crud with SingleWriter mode
        var pengdowsConfig = new DatabaseContextConfiguration
        {
            ConnectionString = _connectionString,
            ProviderName = "Microsoft.Data.Sqlite",
            DbMode = DbMode.SingleWriter // The key feature being tested
        };
        _pengdowsContext = new DatabaseContext(pengdowsConfig, SqliteFactory.Instance);
        _pengdowsGateway = new TableGateway<TestEntity, int>(_pengdowsContext);


        // 2. Setup Dapper
        _dapperConnectionString = _connectionString;


        // 3. Setup EF Core
        _efCoreOptions = new DbContextOptionsBuilder<EfSqliteDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        Console.WriteLine("--- SQLite Concurrency Benchmark ---");
        Console.WriteLine("Tests concurrent writes to a single SQLite database file.");
        Console.WriteLine("Expected: pengdows.crud succeeds, Dapper & EF Core produce locking errors.");
        Console.WriteLine("------------------------------------");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        (_pengdowsContext as IDisposable)?.Dispose();
        // Give time for the file to be released
        for(int i = 0; i < 5; i++)
        {
            try
            {
                if (File.Exists(DbFileName))
                {
                    File.Delete(DbFileName);
                }
                return;
            }
            catch { Task.Delay(100).Wait(); }
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Reset counters for each benchmark iteration
        _pengdowsSuccessCount = 0;
        _pengdowsErrorCount = 0;
        _dapperSuccessCount = 0;
        _dapperErrorCount = 0;
        _efCoreSuccessCount = 0;
        _efCoreErrorCount = 0;
        _pengdowsInsertCounter = 0; // Reset insert counter
    }

    [Benchmark(Description = "pengdows.crud (SingleWriter)")]
    public async Task Pengdows_Concurrency()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(Operations, Parallelism,
            async () =>
            {
                var counterValue = Interlocked.Increment(ref _pengdowsInsertCounter);
                await _pengdowsGateway.CreateAsync(new TestEntity { Name = "pengdows", Counter = counterValue });
                Interlocked.Increment(ref _pengdowsSuccessCount);
            },
            ex =>
            {
                Interlocked.Increment(ref _pengdowsErrorCount);
                Console.WriteLine($"pengdows.crud Error: {ex.GetType().Name}: {ex.Message}");
            });
        Console.WriteLine($"pengdows.crud: {_pengdowsSuccessCount} OK, {_pengdowsErrorCount} Errors");
    }

    [Benchmark(Description = "Dapper (Locking Expected)")]
    public async Task Dapper_Concurrency()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(Operations, Parallelism,
            async () =>
            {
                using (var connection = new SqliteConnection(_dapperConnectionString))
                {
                    await connection.OpenAsync();
                    await connection.ExecuteAsync("INSERT INTO TestEntities (Name, Counter) VALUES (@Name, @Counter);", new { Name = "dapper", Counter = 1 });
                }
                Interlocked.Increment(ref _dapperSuccessCount);
            },
            ex =>
            {
                Interlocked.Increment(ref _dapperErrorCount);
                Console.WriteLine($"Dapper Error: {ex.GetType().Name}: {ex.Message}");
            });
        Console.WriteLine($"Dapper: {_dapperSuccessCount} OK, {_dapperErrorCount} Errors");
    }

    [Benchmark(Description = "EF Core (Locking Expected)")]
    public async Task EFCore_Concurrency()
    {
        await BenchmarkConcurrency.RunConcurrentWithErrors(Operations, Parallelism,
            async () =>
            {
                await using (var context = new EfSqliteDbContext(_efCoreOptions))
                {
                    context.Entities.Add(new TestEntity { Name = "efcore", Counter = 1 });
                    await context.SaveChangesAsync();
                }
                Interlocked.Increment(ref _efCoreSuccessCount);
            },
            ex =>
            {
                Interlocked.Increment(ref _efCoreErrorCount);
                Console.WriteLine($"EF Core Error: {ex.GetType().Name}: {ex.Message}");
            });
        Console.WriteLine($"EF Core: {_efCoreSuccessCount} OK, {_efCoreErrorCount} Errors");
    }
}

// --- Entity and DbContext for the benchmark ---

[Table("TestEntities")]
public class TestEntity
{
    [Id(writable: true)] // Explicitly mark as writable
    [Column("Id", DbType.Int32)]
    public int Id { get; set; }

    [Column("Name", DbType.String)]
    public string Name { get; set; } = string.Empty;

    [Column("Counter", DbType.Int32)]
    public int Counter { get; set; }
}

public class EfSqliteDbContext : DbContext
{
    public EfSqliteDbContext(DbContextOptions<EfSqliteDbContext> options) : base(options) { }

    public DbSet<TestEntity> Entities { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntity>().ToTable("TestEntities");
    }
}
