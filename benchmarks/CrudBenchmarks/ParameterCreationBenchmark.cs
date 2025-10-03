using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ParameterCreationBenchmark
{
    private IDatabaseContext _ctx = null!;
    private int _testValue = 42;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Use FakeDb to avoid actual database connection
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        _ctx = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", factory);
    }

    [Benchmark(Baseline = true)]
    public object ParameterCreation_Dapper()
    {
        // Dapper creates an anonymous object
        return new { id = _testValue };
    }

    [Benchmark]
    public DbParameter ParameterCreation_Mine_Named()
    {
        return _ctx.CreateDbParameter("p0", DbType.Int32, _testValue);
    }

    [Benchmark]
    public DbParameter ParameterCreation_Mine_Unnamed()
    {
        // This uses the parameter name pool
        return _ctx.CreateDbParameter((string?)null, DbType.Int32, _testValue);
    }

    [Benchmark]
    public DbParameter ParameterCreation_Mine_String()
    {
        return _ctx.CreateDbParameter("title", DbType.String, "Test Film");
    }
}
