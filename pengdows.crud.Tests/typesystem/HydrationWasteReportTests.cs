using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;
using Xunit.Abstractions;
using Sample = pengdows.crud.Tests.typesystem.TypeCompletenessTests.Sample;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// For every type on every database and both mapper paths: the bytes allocated per row beyond what the value itself
/// costs to build (the entity and the list are subtracted using an int column on the same database), and the reader
/// calls beyond one per column. Waste is not a timing: both are exact, so an outlier is a fact, not noise.
/// </summary>
[Collection("TypeRegistry")]
public class HydrationWasteReportTests
{
    private const int Rows = 300;

    private readonly ITestOutputHelper _output;

    public HydrationWasteReportTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Counts only the call the mapper made (fakeDb's typed getters call the virtual GetValue themselves).
    private sealed class CountingReader : fakeDbDataReader
    {
        private int _depth;

        public CountingReader(IEnumerable<Dictionary<string, object>> rows) : base(rows)
        {
        }

        public int Calls { get; private set; }

        public List<string> Names { get; } = new();

        private T Hit<T>(string name, Func<T> read)
        {
            if (_depth++ == 0)
            {
                Calls++;
                if (Names.Count < 40)
                {
                    Names.Add(name);
                }
            }

            try
            {
                return read();
            }
            finally
            {
                _depth--;
            }
        }

        public override bool IsDBNull(int o) => Hit("IsDBNull", () => base.IsDBNull(o));
        public override object GetValue(int o) => Hit("GetValue", () => base.GetValue(o));
        public override string GetString(int o) => Hit("GetString", () => base.GetString(o));
        public override int GetInt32(int o) => Hit("GetInt32", () => base.GetInt32(o));
        public override long GetInt64(int o) => Hit("GetInt64", () => base.GetInt64(o));
        public override short GetInt16(int o) => Hit("GetInt16", () => base.GetInt16(o));
        public override byte GetByte(int o) => Hit("GetByte", () => base.GetByte(o));
        public override decimal GetDecimal(int o) => Hit("GetDecimal", () => base.GetDecimal(o));
        public override bool GetBoolean(int o) => Hit("GetBoolean", () => base.GetBoolean(o));
        public override DateTime GetDateTime(int o) => Hit("GetDateTime", () => base.GetDateTime(o));
        public override double GetDouble(int o) => Hit("GetDouble", () => base.GetDouble(o));
        public override float GetFloat(int o) => Hit("GetFloat", () => base.GetFloat(o));
        public override Guid GetGuid(int o) => Hit("GetGuid", () => base.GetGuid(o));
        public override long GetBytes(int o, long a, byte[]? b, int c, int d) => Hit("GetBytes", () => base.GetBytes(o, a, b, c, d));
        public override T GetFieldValue<T>(int o) => Hit("GetFieldValue<" + typeof(T).Name + ">", () => base.GetFieldValue<T>(o));
    }

    private static DatabaseContext Context(SupportedDatabase db, out fakeDbConnection connection)
    {
        var factory = new fakeDbFactory(db);
        connection = new fakeDbConnection { EmulatedProduct = db, ConnectionString = $"Data Source=test;EmulatedProduct={db}" };
        factory.Connections.Add(connection);
        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.SingleConnection
        }, factory, NullLoggerFactory.Instance);
    }

    private static object ProviderShape(object bound, DbType dbType) => bound switch
    {
        System.Text.Json.JsonDocument document => document.RootElement.GetRawText(),
        System.Text.Json.JsonElement element => element.GetRawText(),
        pengdows.crud.types.valueobjects.JsonValue json => json.AsString(),
        Enum member when dbType == DbType.String => member.ToString(),
        Enum member => Convert.ChangeType(member, Enum.GetUnderlyingType(member.GetType())),
        _ => bound
    };

    private static CountingReader ReaderOf(SqlDialect dialect, Sample sample, int rows)
    {
        var data = new List<Dictionary<string, object>>(rows);
        Type? boundType = null;
        for (var i = 0; i < rows; i++)
        {
            var bound = ProviderShape(dialect.CreateDbParameter("p", sample.DbType, sample.Value()).Value!, sample.DbType);
            boundType = bound.GetType();
            data.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["id"] = i, ["v"] = bound });
        }

        return new CountingReader(data)
        {
            Columns = [new fakeDbColumn("id", typeof(int), "INTEGER"), new fakeDbColumn("v", boundType!, "X")]
        };
    }

    private sealed record Measure(double BytesPerRow, double CallsPerRow, string Names);

    private static async Task<Measure> Gateway<TRow>(DatabaseContext ctx, fakeDbConnection connection, SqlDialect dialect,
        Sample sample) where TRow : class, new()
    {
        var gateway = new TableGateway<TRow, int>(ctx);
        var lastNames = new List<string>();
        async Task<(long Bytes, int Calls)> Pass()
        {
            var reader = ReaderOf(dialect, sample, Rows);
            lastNames = reader.Names;
            connection.EnqueueReaderResult(reader);
            await using var container = ctx.CreateSqlContainer("SELECT id, v FROM matrix");
            var before = GC.GetAllocatedBytesForCurrentThread();
            var rows = await gateway.LoadListAsync(container);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(Rows, rows.Count);
            return (bytes, reader.Calls);
        }

        await Pass(); // warm: the plan is built and cached
        var (b, c) = await Pass();
        return new Measure((double)b / Rows, (double)c / Rows, string.Join(",", lastNames.Take(8)));
    }

    private static async Task<Measure> Plan<T>(DatabaseContext ctx, fakeDbConnection connection, SqlDialect dialect, Sample sample)
    {
        var lastNames = new List<string>();
        async Task<(long Bytes, int Calls)> Pass()
        {
            var reader = ReaderOf(dialect, sample, Rows);
            lastNames = reader.Names;
            connection.EnqueueReaderResult(reader);
            await using var container = ctx.CreateSqlContainer("SELECT id, v FROM matrix");
            await using var tracked = await container.ExecuteReaderAsync();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var rows = await DataReaderMapper.LoadAsync<MatrixValue<T>>(tracked, new MapperOptions { Strict = true });
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(Rows, rows.Count);
            return (bytes, reader.Calls);
        }

        await Pass();
        var (b, c) = await Pass();
        return new Measure((double)b / Rows, (double)c / Rows, string.Join(",", lastNames.Take(8)));
    }

    private static readonly MethodInfo GatewayMethod =
        typeof(HydrationWasteReportTests).GetMethod(nameof(Gateway), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo PlanMethod =
        typeof(HydrationWasteReportTests).GetMethod(nameof(Plan), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Task<Measure> Run(MethodInfo open, Type[] args, params object[] arguments) =>
        (Task<Measure>)open.MakeGenericMethod(args).Invoke(null, arguments)!;

    // Bytes per value to construct what the sample is, which hydration has to build too.
    private static double ValueCost(Sample sample)
    {
        var keep = new object?[Rows];
        for (var i = 0; i < Rows; i++)
        {
            keep[i] = sample.Value(); // warm
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rows; i++)
        {
            keep[i] = sample.Value();
        }

        GC.KeepAlive(keep);
        return (double)(GC.GetAllocatedBytesForCurrentThread() - before) / Rows;
    }

    [Fact]
    public async Task Report_WasteForEveryTypeOnEveryDatabase()
    {
        var results = new List<(string Db, string Label, string Path, double ExtraBytes, double ExtraCalls, string Names)>();
        var shapes = new Dictionary<(string Db, string Label), string>();
        var intSample = TypeCompletenessTests.Samples().First(s => s.Label == "int");

        foreach (var db in TypeCompletenessTests.Products())
        {
            using var ctx = Context(db, out var connection);
            var dialect = (SqlDialect)ctx.Dialect;
            var intRow = HydrationMatrixRows.ByDbType[intSample.DbType].MakeGenericType(intSample.Clr);
            var baseGateway = await Run(GatewayMethod, new[] { intRow }, ctx, connection, dialect, intSample);
            var basePlan = await Run(PlanMethod, new[] { intSample.Clr }, ctx, connection, dialect, intSample);

            foreach (var sample in TypeCompletenessTests.Samples())
            {
                if (TypeCompletenessTests.Outcome(sample, dialect) != "ok")
                {
                    continue;
                }

                shapes[(db.ToString(), sample.Label)] =
                    ProviderShape(dialect.CreateDbParameter("p", sample.DbType, sample.Value()).Value!, sample.DbType).GetType().Name;
                var value = ValueCost(sample);
                var row = HydrationMatrixRows.ByDbType[sample.DbType].MakeGenericType(sample.Clr);
                var g = await Run(GatewayMethod, new[] { row }, ctx, connection, dialect, sample);
                var p = await Run(PlanMethod, new[] { sample.Clr }, ctx, connection, dialect, sample);
                results.Add((db.ToString(), sample.Label, "gateway", g.BytesPerRow - baseGateway.BytesPerRow - value, g.CallsPerRow - 2, g.Names));
                results.Add((db.ToString(), sample.Label, "plan", p.BytesPerRow - basePlan.BytesPerRow - value, p.CallsPerRow - 1, p.Names));
            }
        }

        _output.WriteLine($"{results.Count} measurements");
        _output.WriteLine("EXCESS BYTES per row (over the entity, the list and the value itself), worst 60:");
        foreach (var r in results.OrderByDescending(r => r.ExtraBytes).Take(60))
        {
            _output.WriteLine($"  BYTES {r.ExtraBytes,8:F0}  {r.Db}/{r.Label}/{r.Path}");
        }

        _output.WriteLine("EXTRA READER CALLS per row (beyond one per column), worst 40:");
        foreach (var r in results.Where(r => r.ExtraCalls > 0.01).OrderByDescending(r => r.ExtraCalls).Take(40))
        {
            _output.WriteLine($"  CALLS {r.ExtraCalls,6:F1}  {r.Db}/{r.Label}/{r.Path}");
        }

        _output.WriteLine("EXTRA CALLS BY TYPE AND SHAPE (databases that still pay one or more extra reader calls):");
        foreach (var group in results.Where(r => r.ExtraCalls > 0.01)
                     .GroupBy(r => (r.Label, r.Path, Shape: shapes[(r.Db, r.Label)]))
                     .OrderBy(g => g.Key.Label))
        {
            _output.WriteLine($"  SHAPE {group.Key.Label,-14} {group.Key.Path,-7} bound as {group.Key.Shape,-12} calls [{group.First().Names}] on {string.Join(",", group.Select(r => r.Db).Distinct())}");
        }

        // By type across databases: the typical excess, so a per-type cause is visible.
        _output.WriteLine("BY TYPE (median excess bytes / median extra calls, gateway | plan):");
        foreach (var label in results.Select(r => r.Label).Distinct())
        {
            double Med(IEnumerable<double> xs)
            {
                var a = xs.OrderBy(x => x).ToArray();
                return a.Length == 0 ? 0 : a[a.Length / 2];
            }

            var gw = results.Where(r => r.Label == label && r.Path == "gateway").ToList();
            var pl = results.Where(r => r.Label == label && r.Path == "plan").ToList();
            _output.WriteLine($"  TYPE  {label,-22} gw {Med(gw.Select(r => r.ExtraBytes)),7:F0}B {Med(gw.Select(r => r.ExtraCalls)),4:F1}c | plan {Med(pl.Select(r => r.ExtraBytes)),7:F0}B {Med(pl.Select(r => r.ExtraCalls)),4:F1}c");
        }
    }
}
