// =============================================================================
// FILE: AuditOpportunityBenchmarks.cs
// PURPOSE: One benchmark per performance opportunity from the 2.0.6 code audit, so each suspected cost is measured before and
//          after its change. fakeDb only: no Docker, no real engine.
//
// AI SUMMARY:
// - BatchUpdate: a batch update of RowCount rows (per-cell parameter creation and boxing).
// - ResolveRowMapper: the per-result-set row mapper resolution (an expression compile per call).
// - LoadSingle: DataReaderMapper.LoadAsync over one result set (the per-query shape build).
// - PositionalExecute: one execution of a positional statement with three parameters (the per-execution hash set).
// - ConvertWithCache: the per-insert generated-id conversion.
// =============================================================================

using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class AuditOpportunityBenchmarks
{
    [Table("audit_rows")]
    public class AuditRow
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = "";

        [Column("amount", DbType.Int32)]
        public int Amount { get; set; }

        [Column("active", DbType.Boolean)]
        public bool Active { get; set; }
    }

    private DatabaseContext _ctx = null!;
    private DatabaseContext _positionalCtx = null!;
    private fakeDbConnection _exec = null!;
    private TableGateway<AuditRow, int> _gateway = null!;
    private AuditRow[] _rows = null!;
    private IColumnInfo _idColumn = null!;

    [Params(200)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _ctx = Context(SupportedDatabase.PostgreSql, out _exec);
        _gateway = new TableGateway<AuditRow, int>(_ctx);
        var tableInfo = (ITableInfo)typeof(TableGateway<AuditRow, int>).BaseType!
            .GetField("_tableInfo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(_gateway)!;
        _idColumn = tableInfo.OrderedColumns[1];
        _positionalCtx = Context(SupportedDatabase.Informix, out _);
        _rows = Enumerable.Range(1, RowCount)
            .Select(i => new AuditRow { Id = i, Name = "row " + i, Amount = i * 3, Active = (i & 1) == 0 })
            .ToArray();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _ctx.Dispose();
        _positionalCtx.Dispose();
    }

    private static DatabaseContext Context(SupportedDatabase product, out fakeDbConnection exec)
    {
        var factory = new fakeDbFactory(product);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = product });
        exec = new fakeDbConnection { EmulatedProduct = product };
        factory.Connections.Add(exec);
        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Server=x;Database=y;EmulatedProduct={product}",
            DbMode = DbMode.Standard,
        }, factory);
    }

    private static fakeDbDataReader OneRowReader(int rows) =>
        new(Enumerable.Range(1, rows).Select(i => new Dictionary<string, object>
        {
            ["id"] = i, ["name"] = "row", ["amount"] = i, ["active"] = true,
        }));

    [Benchmark]
    public Task<int> BatchUpdate() => _gateway.BatchUpdateAsync(_rows, _ctx).AsTask();

    [Benchmark]
    public object ResolveRowMapper()
    {
        using var reader = OneRowReader(1);
        reader.Read();
        return DataReaderMapper.ResolveRowMapper<AuditRow>(reader, MapperOptions.Default);
    }

    [Benchmark]
    public async Task<List<AuditRow>> LoadSingle()
    {
        using var reader = OneRowReader(1);
        return await DataReaderMapper.LoadAsync<AuditRow>(reader, MapperOptions.Default);
    }

    [Benchmark]
    public async Task<int> PositionalExecute()
    {
        {
            await using var sc = _positionalCtx.CreateSqlContainer();
            var a = sc.AddParameterWithValue("a", DbType.Int32, 1);
            var b = sc.AddParameterWithValue("b", DbType.String, "x");
            var c = sc.AddParameterWithValue("c", DbType.Int32, 2);
            sc.Query.Append("INSERT INTO audit_rows (id, name, amount) VALUES (")
                .Append(sc.MakeParameterName(a)).Append(", ")
                .Append(sc.MakeParameterName(b)).Append(", ")
                .Append(sc.MakeParameterName(c))
                .Append(')');
            return await sc.ExecuteNonQueryAsync();
        }
    }

    [Benchmark]
    public int ConvertWithCache() => (int)pengdows.crud.types.coercion.ConverterResolution.ConvertWithCache(42L, typeof(int));

    // The two parts of a batch update's per-cell work, measured apart from the SQL they feed.
    private static readonly (DbType Type, Func<AuditRow, object?> Value)[] Cells =
    {
        (DbType.Int32, r => r.Id),
        (DbType.String, r => r.Name),
        (DbType.Int32, r => r.Amount),
        (DbType.Boolean, r => r.Active),
    };

    [Benchmark]
    public int ParameterCreation()
    {
        var count = 0;
        foreach (var row in _rows)
        {
            foreach (var (type, value) in Cells)
            {
                _ctx.Dialect.CreateDbParameter("p" + count, type, value(row));
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    public object?[] CellExtraction()
    {
        var columns = _gateway.GetType().GetField("_tableInfo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return pengdows.crud.BaseTableGateway<AuditRow>.ExtractBatchCells(_rows, ((ITableInfo)columns.GetValue(_gateway)!).OrderedColumns);
    }

    [Benchmark]
    public int BuildBatchUpdate() => _gateway.BuildBatchUpdate(_rows, _ctx).Count;

    // Reader alone: the cost LoadSingle pays before any mapping (reader construction and one Read).
    [Benchmark]
    public bool ReaderOnly()
    {
        using var reader = OneRowReader(1);
        return reader.Read();
    }

    // One single-row insert on fakeDb: the scale the per-insert conversion is measured against.
    [Benchmark]
    public Task<bool> CreateSingle() => _gateway.CreateAsync(_rows[0], _ctx).AsTask();

    // The pool's per-return cap check (ConcurrentQueue.Count), on a queue at its cap of 100.
    private static readonly System.Collections.Concurrent.ConcurrentQueue<DbParameter> FullPool = Fill();

    private static System.Collections.Concurrent.ConcurrentQueue<DbParameter> Fill()
    {
        var q = new System.Collections.Concurrent.ConcurrentQueue<DbParameter>();
        for (var i = 0; i < 100; i++)
        {
            q.Enqueue(new fakeDbParameter());
        }

        return q;
    }

    [Benchmark]
    public int QueueCount() => FullPool.Count;

    // The batch's defensive copy of the entity list before chunking (TableGateway.Core.cs ~1220).
    [Benchmark]
    public int ListCopy() => _rows.ToList().Count;

    // The per-cell name for the array-parameter path (TableGateway.Batch.cs ~491).
    [Benchmark]
    public int RowName() => $"p_row{RowCount}".Length;

    // A wide row (16 columns): the per-query shape cost scales with the column count, so this is the case the rented shape targets.
    [Table("audit_wide")]
    public class WideRow
    {
        [Id] [Column("c0", DbType.Int32)] public int C0 { get; set; }
        [Column("c1", DbType.Int32)] public int C1 { get; set; }
        [Column("c2", DbType.Int32)] public int C2 { get; set; }
        [Column("c3", DbType.Int32)] public int C3 { get; set; }
        [Column("c4", DbType.Int32)] public int C4 { get; set; }
        [Column("c5", DbType.Int32)] public int C5 { get; set; }
        [Column("c6", DbType.Int32)] public int C6 { get; set; }
        [Column("c7", DbType.Int32)] public int C7 { get; set; }
        [Column("c8", DbType.Int32)] public int C8 { get; set; }
        [Column("c9", DbType.Int32)] public int C9 { get; set; }
        [Column("c10", DbType.Int32)] public int C10 { get; set; }
        [Column("c11", DbType.Int32)] public int C11 { get; set; }
        [Column("c12", DbType.Int32)] public int C12 { get; set; }
        [Column("c13", DbType.Int32)] public int C13 { get; set; }
        [Column("c14", DbType.Int32)] public int C14 { get; set; }
        [Column("c15", DbType.Int32)] public int C15 { get; set; }
    }

    private static fakeDbDataReader WideReader() =>
        new(new[]
        {
            Enumerable.Range(0, 16).ToDictionary(i => "c" + i, i => (object)i),
        });

    [Benchmark]
    public async Task<List<WideRow>> LoadWide()
    {
        using var reader = WideReader();
        return await DataReaderMapper.LoadAsync<WideRow>(reader, MapperOptions.Default);
    }

    [Benchmark]
    public bool ReaderOnlyWide()
    {
        using var reader = WideReader();
        return reader.Read();
    }

    // Item 4 isolated: the positional path's dedupe of parameter names (SqlContainer.cs ~833), on the same three-name sequence
    // PositionalExecute binds. Copied in shape; the real comparer is private, so OrdinalIgnoreCase stands in for it.
    private static readonly string[] Names = { "p0", "p1", "p2" };

    [Benchmark(Baseline = true)]
    public int DedupeHashSetLazy()
    {
        HashSet<string>? seen = null;
        var firsts = 0;
        foreach (var name in Names)
        {
            seen ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (seen.Add(name))
            {
                firsts++;
            }
        }

        return firsts;
    }

    [Benchmark]
    public int DedupeListScan()
    {
        List<string>? seen = null;
        var firsts = 0;
        foreach (var name in Names)
        {
            seen ??= new List<string>(Names.Length);
            var duplicate = false;
            foreach (var s in seen)
            {
                if (string.Equals(s, name, StringComparison.OrdinalIgnoreCase))
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
            {
                seen.Add(name);
                firsts++;
            }
        }

        return firsts;
    }

    // Item 1 floor: the same 800 parameters built as bare fakeDb parameters, with no dialect work.
    [Benchmark]
    public int ParameterFloor()
    {
        var count = 0;
        foreach (var row in _rows)
        {
            foreach (var (type, value) in Cells)
            {
                var p = new fakeDbParameter { ParameterName = "p" + count, DbType = type, Value = value(row) };
                count += p.ParameterName.Length > 0 ? 1 : 0;
            }
        }

        return count;
    }

    // Item 1 split: the same 800 int values through the dialect, typed (T = int) and boxed (T = object, as the batch passes them).
    [Benchmark]
    public int DialectTypedInt()
    {
        var count = 0;
        for (var i = 0; i < RowCount * 4; i++)
        {
            _ctx.Dialect.CreateDbParameter<int>("p" + i, DbType.Int32, i);
            count++;
        }

        return count;
    }

    [Benchmark]
    public int DialectBoxedInt()
    {
        var count = 0;
        for (var i = 0; i < RowCount * 4; i++)
        {
            object boxed = i;
            _ctx.Dialect.CreateDbParameter<object?>("p" + i, DbType.Int32, boxed);
            count++;
        }

        return count;
    }

    // Item 1 split: the column marking the batch runs on every bound cell.
    [Benchmark]
    public int MarkEveryCell()
    {
        var column = _idColumn;
        var p = _ctx.Dialect.CreateDbParameter<int>("p0", DbType.Int32, 1);
        var count = 0;
        for (var i = 0; i < RowCount * 4; i++)
        {
            ((pengdows.crud.dialects.SqlDialect)_ctx.Dialect).MarkColumnParameter(p, column);
            count++;
        }

        return count;
    }
}
