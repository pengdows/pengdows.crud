using System.Data;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace CrudBenchmarks;

/// <summary>
/// Benchmarks <c>CreateDbParameter&lt;Guid&gt;</c> across dialects that use the
/// unified <c>GuidStorageFormat</c> path.
///
/// Scenarios covered:
///   PassThrough  — SQL Server (DbType.Guid, provider-native)
///   String       — SQLite / DuckDB / Oracle / Snowflake (DbType.String, 36-char "D" format)
///   Binary       — Firebird default (DbType.Binary, ToByteArray)
///
/// All dialects use fakeDb so there is no I/O — pure parameter-creation overhead.
/// Run with:
///   CRUD_BENCH_INPROC=1 dotnet run -c Release --project benchmarks/CrudBenchmarks \
///     -- --filter "*GuidParam*"
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class BenchmarkGuidParameters
{
    private static readonly Guid _guid = new("550e8400-e29b-41d4-a716-446655440000");

    // Dialects under test — created once in GlobalSetup to eliminate ctor cost from measurements.
    private SqlServerDialect _sqlServer = null!;
    private SqliteDialect _sqlite = null!;
    private DuckDbDialect _duckDb = null!;
    private OracleDialect _oracle = null!;
    private FirebirdDialect _fbBinary = null!;
    private FirebirdDialect _fbString = null!;

    [GlobalSetup]
    public void Setup()
    {
        _sqlServer = new SqlServerDialect(
            new fakeDbFactory(SupportedDatabase.SqlServer),
            NullLoggerFactory.Instance.CreateLogger(nameof(SqlServerDialect)));

        _sqlite = new SqliteDialect(
            new fakeDbFactory(SupportedDatabase.Sqlite),
            NullLoggerFactory.Instance.CreateLogger(nameof(SqliteDialect)));

        _duckDb = new DuckDbDialect(
            new fakeDbFactory(SupportedDatabase.DuckDB),
            NullLoggerFactory.Instance.CreateLogger(nameof(DuckDbDialect)));

        _oracle = new OracleDialect(
            new fakeDbFactory(SupportedDatabase.Oracle),
            NullLoggerFactory.Instance.CreateLogger(nameof(OracleDialect)));

        _fbBinary = new FirebirdDialect(
            new fakeDbFactory(SupportedDatabase.Firebird),
            NullLogger.Instance);

        _fbString = new FirebirdDialect(
            new fakeDbFactory(SupportedDatabase.Firebird),
            NullLogger.Instance)
        {
            GuidStorageMode = FirebirdGuidStorageMode.String
        };
    }

    [Benchmark(Baseline = true, Description = "SqlServer (PassThrough)")]
    public object SqlServer_PassThrough()
    {
        var p = _sqlServer.CreateDbParameter("id", DbType.Guid, _guid);
        return p.Value!;
    }

    [Benchmark(Description = "SQLite (String)")]
    public object Sqlite_String()
    {
        var p = _sqlite.CreateDbParameter("id", DbType.Guid, _guid);
        return p.Value!;
    }

    [Benchmark(Description = "DuckDB (String)")]
    public object DuckDb_String()
    {
        var p = _duckDb.CreateDbParameter("id", DbType.Guid, _guid);
        return p.Value!;
    }

    [Benchmark(Description = "Oracle (String)")]
    public object Oracle_String()
    {
        var p = _oracle.CreateDbParameter("id", DbType.Guid, _guid);
        return p.Value!;
    }

    [Benchmark(Description = "Firebird Binary")]
    public object Firebird_Binary()
    {
        var p = _fbBinary.CreateDbParameter("id", DbType.Guid, _guid);
        return p.Value!;
    }

    [Benchmark(Description = "Firebird String")]
    public object Firebird_String()
    {
        var p = _fbString.CreateDbParameter("id", DbType.Guid, _guid);
        return p.Value!;
    }

    /// <summary>
    /// High-frequency loop to measure amortised cost over 1 000 parameters —
    /// mimics a bulk-insert workload where Guid columns appear in every row.
    /// </summary>
    [Benchmark(Description = "SQLite 1k Guid params")]
    public int Sqlite_1k_GuidParams()
    {
        var sum = 0;
        for (var i = 0; i < 1_000; i++)
        {
            var p = _sqlite.CreateDbParameter(null, DbType.Guid, _guid);
            sum += ((string)p.Value!).Length; // prevent dead-code elimination
        }

        return sum;
    }
}
