using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using pengdows.stormgate.EntityFrameworkCore;
using pengdows.stormgate;

namespace DuckDbEfCoreBenchmarks;

/// <summary>
/// DuckDB EF Core comparison using Skirrels' DuckDB.EFCoreProvider through StormGate.
/// The existing DuckDB equal-footing report supplies the pengdows and Dapper rows; this
/// isolated net10 benchmark supplies EF Core 10 raw LINQ, compiled LINQ, and SaveChanges
/// rows without forcing the main net8/net10 benchmark project onto EF Core 10 dependencies.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class DuckDbEfCoreEqualFootingBenchmarks : IAsyncDisposable
{
    private const int SeedRows = 1000;
    private string _databaseFile = null!;
    private string _connectionString = null!;
    private StormGate _stormGate = null!;
    private StormGateConnectionInterceptor _interceptor = null!;
    private int _createId;

    [Params(1, 100)]
    public int RecordCount { get; set; }

    private static readonly Func<DuckDbBenchContext, int, Task<DuckDbBenchEntity?>> CompiledReadSingle =
        EF.CompileAsyncQuery((DuckDbBenchContext context, int id) =>
            context.Benchmarks.AsNoTracking().FirstOrDefault(entity => entity.Id == id));

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _databaseFile = Path.Combine(Path.GetTempPath(), $"pengdows_duckdb_ef_{Guid.NewGuid():N}.duckdb");
        _connectionString = $"Data Source={_databaseFile}";

        await using (var connection = new DuckDBConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE benchmark (
                    id INTEGER PRIMARY KEY,
                    name VARCHAR NOT NULL,
                    age INTEGER NOT NULL,
                    salary DOUBLE NOT NULL,
                    is_active BOOLEAN NOT NULL,
                    created_at TIMESTAMP NOT NULL
                )
                """;
            await command.ExecuteNonQueryAsync();

            command.CommandText = "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES ($id, $name, $age, $salary, $active, $created)";
            var id = command.CreateParameter();
            id.ParameterName = "id";
            var name = command.CreateParameter();
            name.ParameterName = "name";
            var age = command.CreateParameter();
            age.ParameterName = "age";
            var salary = command.CreateParameter();
            salary.ParameterName = "salary";
            var active = command.CreateParameter();
            active.ParameterName = "active";
            var created = command.CreateParameter();
            created.ParameterName = "created";
            command.Parameters.AddRange(new[] { id, name, age, salary, active, created });

            for (var row = 1; row <= SeedRows; row++)
            {
                id.Value = row;
                name.Value = $"Person {row}";
                age.Value = 20 + row % 50;
                salary.Value = 30_000.0 + row * 100.0;
                active.Value = row % 2 == 0;
                created.Value = DateTime.UtcNow.AddDays(-row);
                await command.ExecuteNonQueryAsync();
            }
        }

        _createId = 10_000;
        _stormGate = StormGate.Create(
            DuckDBClientFactory.Instance,
            _connectionString,
            maxConcurrentOpens: 8,
            acquireTimeout: TimeSpan.FromSeconds(30));
        _interceptor = new StormGateConnectionInterceptor(_stormGate);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup() => await DisposeAsync();

    [Benchmark]
    public async Task<DuckDbBenchEntity?> ReadSingle_EntityFramework_StormGate()
    {
        DuckDbBenchEntity? result = null;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var context = CreateContext();
            result = await context.Benchmarks.AsNoTracking()
                .FirstOrDefaultAsync(entity => entity.Id == (i % SeedRows) + 1);
        }

        return result;
    }

    [Benchmark]
    public async Task<DuckDbBenchEntity?> ReadSingle_EntityFramework_Compiled_StormGate()
    {
        DuckDbBenchEntity? result = null;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var context = CreateContext();
            result = await CompiledReadSingle(context, (i % SeedRows) + 1);
        }

        return result;
    }

    [Benchmark]
    public async Task<DuckDbBenchEntity?> ReadSingle_EntityFramework_Compiled_NoStormGate()
    {
        DuckDbBenchEntity? result = null;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var context = CreateContext(useStormGate: false);
            result = await CompiledReadSingle(context, (i % SeedRows) + 1);
        }

        return result;
    }

    [Benchmark]
    public async Task<List<DuckDbBenchEntity>> ReadList_EntityFramework_StormGate()
    {
        List<DuckDbBenchEntity> result = [];
        for (var i = 0; i < RecordCount; i++)
        {
            await using var context = CreateContext();
            result = await context.Benchmarks.AsNoTracking()
                .Where(entity => entity.Age > 0)
                .Take(RecordCount)
                .ToListAsync();
        }

        return result;
    }

    [Benchmark]
    public async Task<int> Create_EntityFramework_SaveChanges_StormGate()
    {
        var count = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var context = CreateContext();
            context.Benchmarks.Add(new DuckDbBenchEntity
            {
                Id = Interlocked.Increment(ref _createId),
                Name = $"Created {i}",
                Age = 25,
                Salary = 50_000.0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            count += await context.SaveChangesAsync();
        }

        return count;
    }

    private DuckDbBenchContext CreateContext(bool useStormGate = true)
    {
        var connection = new DuckDBConnection(_connectionString);
        var optionsBuilder = new DbContextOptionsBuilder<DuckDbBenchContext>()
            .UseDuckDB(connection, contextOwnsConnection: true);
        if (useStormGate)
            optionsBuilder.UseStormGate(_interceptor);

        var options = optionsBuilder.Options;
        return new DuckDbBenchContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        _stormGate?.Dispose();
        foreach (var path in new[] { _databaseFile, _databaseFile + ".wal" })
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                await Task.Yield();
            }
        }
    }

    private sealed class DuckDbBenchContext(DbContextOptions<DuckDbBenchContext> options) : DbContext(options)
    {
        public DbSet<DuckDbBenchEntity> Benchmarks => Set<DuckDbBenchEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<DuckDbBenchEntity>();
            entity.ToTable("benchmark");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Name).HasColumnName("name");
            entity.Property(row => row.Age).HasColumnName("age");
            entity.Property(row => row.Salary).HasColumnName("salary");
            entity.Property(row => row.IsActive).HasColumnName("is_active");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        }
    }

    public sealed class DuckDbBenchEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public int Age { get; set; }
        public double Salary { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
