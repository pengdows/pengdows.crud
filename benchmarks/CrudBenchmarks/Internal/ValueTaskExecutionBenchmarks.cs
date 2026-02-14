using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace CrudBenchmarks.Internal;

[MemoryDiagnoser]
public class ValueTaskExecutionBenchmarks
{
    private DatabaseContext? _context;
    private ISqlContainer? _container;

    [GlobalSetup]
    public void Setup()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        _context = new DatabaseContext(cfg, factory);
        _container = _context.CreateSqlContainer("SELECT 1");
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }

        if (_context != null)
        {
            await _context.DisposeAsync();
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<int> ExecuteNonQuery_ValueTask()
    {
        if (_container == null)
        {
            throw new InvalidOperationException("Container not initialized.");
        }

        return await _container.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    [Benchmark]
    public async Task<int> ExecuteNonQuery_ValueTask_AsTask()
    {
        if (_container == null)
        {
            throw new InvalidOperationException("Container not initialized.");
        }

        return await _container.ExecuteNonQueryAsync().AsTask().ConfigureAwait(false);
    }
}
