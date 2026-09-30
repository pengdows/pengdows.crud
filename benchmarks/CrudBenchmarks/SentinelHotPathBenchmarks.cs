using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;

namespace CrudBenchmarks;

/// <summary>
/// Isolates the sentinel reads PreventDatabaseUnload performs on every connection acquisition
/// (health check over the sentinel list) and release (is this connection a sentinel?). Best selects
/// this mode for LocalDB, Firebird and Db2 LUW. Review 2026-09-29: these reads copied the list under a
/// lock (and ran a LINQ closure on release); they are now lock-free reads of an immutable array.
/// fakeDb only: no database round trip, so the measurement is the library's own per-operation cost.
/// </summary>
[MemoryDiagnoser]
public class SentinelHotPathBenchmarks
{
    private DatabaseContext _context = null!;
    private ITrackedConnection _sentinel = null!;
    private ITrackedConnection _ordinary = null!;

    [GlobalSetup]
    public void Setup()
    {
        _context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=localhost;Database=/data/bench.fdb;EmulatedProduct=Firebird",
            DbMode = DbMode.Best
        }, new fakeDbFactory(SupportedDatabase.Firebird));
        _sentinel = _context.GetSentinelSnapshot()[0].Connection;
        _ordinary = _context.FactoryCreateConnection();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _ordinary.Dispose();
        _context.Dispose();
    }

    // Acquisition-side read: walk the snapshot, as the health check does.
    [Benchmark]
    public int AcquireHealthCheckRead()
    {
        var open = 0;
        foreach (var (connection, _) in _context.GetSentinelSnapshot())
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                open++;
            }
        }

        return open;
    }

    // Release-side read for an ordinary working connection (the common case).
    [Benchmark]
    public bool ReleaseIsSentinelCheck() => _context.IsSentinel(_ordinary);
}
