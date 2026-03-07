using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

/// <summary>
/// Focused SQLite benchmark to understand DateTime handling costs.
/// Uses a string-backed DATETIME column to reflect SQLite storage behavior.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SqliteDateHandlingBenchmarks : IDisposable
{
    private const int SeedRows = 1000;

    private SqliteConnection _sentinel = null!;
    private SqliteConnection _dapperConn = null!;   // dedicated always-open connection, mirrors pengdows SingleConnection mode
    private DatabaseContext _context = null!;
    private TableGateway<BenchEntity, long> _gateway = null!;
    private TypeMapRegistry _typeMap = null!;
    private string _connectionString = string.Empty;
    private string _pengdowsSql = string.Empty;
    private string _dapperSql = string.Empty;
    private int _idSeed;
    private string? _fieldTypeDump;

    [Params(1, 10, 100)] public int RecordCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _connectionString = "Data Source=SqliteDateHandlingBench;Mode=Memory;Cache=Shared";

        _sentinel = new SqliteConnection(_connectionString);
        _sentinel.Open();

        using (var cmd = _sentinel.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS benchmark (
                    id INTEGER PRIMARY KEY,
                    created_at TEXT NOT NULL
                )";
            cmd.ExecuteNonQuery();
        }

        using (var tx = _sentinel.BeginTransaction())
        {
            using var cmd = _sentinel.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO benchmark (id, created_at) VALUES (@id, @created_at)";
            var idParam = cmd.CreateParameter();
            idParam.ParameterName = "@id";
            idParam.DbType = DbType.Int32;
            var createdParam = cmd.CreateParameter();
            createdParam.ParameterName = "@created_at";
            createdParam.DbType = DbType.String;
            cmd.Parameters.Add(idParam);
            cmd.Parameters.Add(createdParam);
            for (var i = 1; i <= SeedRows; i++)
            {
                idParam.Value = i;
                createdParam.Value = DateTime.UtcNow.AddMinutes(-i).ToString("O");
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        _typeMap = new TypeMapRegistry();
        _typeMap.Register<BenchEntity>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = _connectionString,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.SingleConnection
        };
        _context = new DatabaseContext(cfg, SqliteFactory.Instance, null, _typeMap);
        _gateway = new TableGateway<BenchEntity, long>(_context);

        _pengdowsSql = BuildSingleReadSql(p => _context.MakeParameterName(p));
        _dapperSql = BuildSingleReadSql(p => $"@{p}");

        // Open a dedicated connection for Dapper — equivalent to pengdows SingleConnection mode.
        _dapperConn = new SqliteConnection(_connectionString);
        _dapperConn.Open();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _context?.Dispose();
        _dapperConn?.Dispose();
        _sentinel?.Dispose();
    }

    public void Dispose()
    {
        GlobalCleanup();
    }

    [Benchmark]
    public async Task<int> ReadSingle_Pengdows_PureMapping()
    {
        var hits = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = NextId();
            await using var container = _context.CreateSqlContainer(_pengdowsSql);
            container.AddParameterWithValue("id", DbType.Int64, (long)id);
            var result = await _gateway.LoadSingleAsync(container);
            if (result != null)
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark(Baseline = true)]
    public async Task<int> ReadSingle_Dapper_PureMapping()
    {
        var hits = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = NextId();
            var row = await _dapperConn.QuerySingleOrDefaultAsync<DapperBenchEntity>(_dapperSql, new { id });
            if (row != null)
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark]
    public string DumpFieldTypes()
    {
        if (_fieldTypeDump != null)
        {
            return _fieldTypeDump;
        }

        using var cmd = _sentinel.CreateCommand();
        cmd.CommandText = _dapperSql;
        var param = cmd.CreateParameter();
        param.ParameterName = "@id";
        param.DbType = DbType.Int32;
        param.Value = 1;
        cmd.Parameters.Add(param);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            _fieldTypeDump = "No rows returned.";
            Console.WriteLine(_fieldTypeDump);
            return _fieldTypeDump;
        }

        var props = typeof(BenchEntity).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var propLookup = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in props)
        {
            propLookup[prop.Name] = prop;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            var fieldType = reader.GetFieldType(i);
            propLookup.TryGetValue(columnName, out var prop);

            sb.Append(columnName);
            sb.Append(": field=");
            sb.Append(fieldType.Name);
            sb.Append(", property=");
            sb.Append(prop?.PropertyType.Name ?? "n/a");
            sb.AppendLine();
        }

        _fieldTypeDump = sb.ToString();
        Console.WriteLine(_fieldTypeDump);
        return _fieldTypeDump;
    }

    private int NextId()
    {
        var value = Interlocked.Increment(ref _idSeed);
        var normalized = (value % SeedRows) + 1;
        return normalized;
    }

    private static string BuildSingleReadSql(Func<string, string> param)
    {
        return "SELECT id, created_at FROM benchmark WHERE id = " + param("id");
    }

    [Table("benchmark")]
    public sealed class BenchEntity
    {
        // SQLite stores INTEGER as 64-bit; using long avoids an int64→int32 narrowing
        // conversion on every read — the id column contributes zero coercion overhead.
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }
    }

    public sealed class DapperBenchEntity
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}