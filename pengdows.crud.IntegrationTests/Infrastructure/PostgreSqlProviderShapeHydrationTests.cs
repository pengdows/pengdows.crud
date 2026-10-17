using System.Data;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using NpgsqlTypes;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// What Npgsql actually returns for the PostgreSQL-family types (NpgsqlRange, NpgsqlInterval, NpgsqlInet, NpgsqlCidr,
/// PhysicalAddress, arrays, hstore), run through both real mapper paths on a PostgreSQL fakeDb context: the value read
/// is the value stored, each column costs one reader call, and a row allocates little more than the value it builds.
/// No database is needed; Npgsql's own types are loaded, which the unit-test project deliberately avoids.
/// </summary>
public class PostgreSqlProviderShapeHydrationTests
{
    private readonly ITestOutputHelper _output;

    public PostgreSqlProviderShapeHydrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Table("pg_shape")]
    public sealed class Row<T>
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Object)] public T V { get; set; } = default!;
    }

    public sealed class Plain<T>
    {
        public T V { get; set; } = default!;
    }

    // Counts only the call the mapper made (fakeDb's typed getters call the virtual GetValue themselves).
    private sealed class CountingReader : fakeDbDataReader
    {
        private int _depth;

        public CountingReader(IEnumerable<Dictionary<string, object>> rows) : base(rows)
        {
        }

        public int Calls { get; private set; }

        private TResult Hit<TResult>(Func<TResult> read)
        {
            if (_depth++ == 0)
            {
                Calls++;
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

        public override bool IsDBNull(int o) => Hit(() => base.IsDBNull(o));
        public override object GetValue(int o) => Hit(() => base.GetValue(o));
        public override string GetString(int o) => Hit(() => base.GetString(o));
        public override int GetInt32(int o) => Hit(() => base.GetInt32(o));
        public override string GetDataTypeName(int o) => Hit(() => base.GetDataTypeName(o));
        public override long GetBytes(int o, long a, byte[]? b, int c, int d) => Hit(() => base.GetBytes(o, a, b, c, d));
        public override TResult GetFieldValue<TResult>(int o) => Hit(() => base.GetFieldValue<TResult>(o));
    }

    private static DatabaseContext Context(out fakeDbConnection connection)
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        connection = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.PostgreSql,
            ConnectionString = "Data Source=test;EmulatedProduct=PostgreSql"
        };
        factory.Connections.Add(connection);
        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=PostgreSql",
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.SingleConnection
        }, factory, NullLoggerFactory.Instance);
    }

    private const int Rows = 300;

    private static readonly DateTime Lower = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Upper = new(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    // One provider shape: what Npgsql returns (fresh each call), its column type name, the property it is read into,
    // and what it must read as.
    private sealed record Shape(string Name, Func<object> Stored, string DataTypeName, Type Property, Func<object> Expected);

    private static readonly Shape[] Table =
    [
        new("int4range", () => new NpgsqlRange<int>(1, true, 10, false), "int4range", typeof(Range<int>), () => new Range<int>(1, 10, true, false)),
        new("int8range", () => new NpgsqlRange<long>(1, true, 10, false), "int8range", typeof(Range<long>), () => new Range<long>(1, 10, true, false)),
        new("numrange", () => new NpgsqlRange<decimal>(1.25m, true, 9.5m, false), "numrange", typeof(Range<decimal>), () => new Range<decimal>(1.25m, 9.5m, true, false)),
        new("daterange", () => new NpgsqlRange<DateTime>(Lower, true, Upper, false), "daterange", typeof(Range<DateTime>), () => new Range<DateTime>(Lower, Upper, true, false)),
        new("tstzrange", () => new NpgsqlRange<DateTime>(Lower, true, Upper, false), "tstzrange", typeof(Range<DateTimeOffset>),
            () => new Range<DateTimeOffset>(new DateTimeOffset(Lower), new DateTimeOffset(Upper), true, false)),
        new("interval", () => new NpgsqlInterval(14, 3, 3_723_123_456), "interval", typeof(PostgreSqlInterval), () => new PostgreSqlInterval(14, 3, 3_723_123_456)),
        new("inet", () => new NpgsqlInet(IPAddress.Parse("192.168.0.1"), 24), "inet", typeof(Inet), () => Inet.Parse("192.168.0.1/24")),
        new("cidr", () => new NpgsqlCidr(IPAddress.Parse("10.0.0.0"), 8), "cidr", typeof(Cidr), () => Cidr.Parse("10.0.0.0/8")),
        new("macaddr", () => PhysicalAddress.Parse("08002B010203"), "macaddr", typeof(MacAddress), () => MacAddress.Parse("08:00:2b:01:02:03")),
        new("int4[]", () => new[] { 1, -2, 3 }, "integer[]", typeof(int[]), () => new[] { 1, -2, 3 }),
        new("text[]", () => new[] { "a", "b" }, "text[]", typeof(string[]), () => new[] { "a", "b" }),
        new("hstore", () => new Dictionary<string, string?> { ["a"] = "1", ["b"] = null }, "hstore", typeof(HStore),
            () => HStore.Parse("\"a\"=>\"1\", \"b\"=>NULL"))
    ];

    public static IEnumerable<object[]> Names() => Table.Select(s => new object[] { s.Name });

    private static Shape Of(string name) => Table.Single(s => s.Name == name);

    private static CountingReader ReaderOf(Shape shape, int rows)
    {
        var data = new List<Dictionary<string, object>>(rows);
        Type? storedType = null;
        for (var i = 0; i < rows; i++)
        {
            var stored = shape.Stored();
            storedType = stored.GetType();
            data.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["id"] = i, ["v"] = stored });
        }

        return new CountingReader(data)
        {
            Columns = [new fakeDbColumn("id", typeof(int), "integer"), new fakeDbColumn("v", storedType!, shape.DataTypeName)]
        };
    }

    private sealed record Run(List<object?> Values, long Bytes, int Calls);

    private static async Task<Run> ThroughTheGateway<T>(DatabaseContext ctx, fakeDbConnection connection, Shape shape, int rows)
    {
        var gateway = new TableGateway<Row<T>, int>(ctx);
        var reader = ReaderOf(shape, rows);
        connection.EnqueueReaderResult(reader);
        await using var container = ctx.CreateSqlContainer("SELECT id, v FROM pg_shape");
        var before = GC.GetAllocatedBytesForCurrentThread();
        var loaded = await gateway.LoadListAsync(container);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        return new Run(loaded.Select(r => (object?)r.V).ToList(), bytes, reader.Calls);
    }

    private static async Task<Run> ThroughThePlan<T>(DatabaseContext ctx, fakeDbConnection connection, Shape shape, int rows)
    {
        var reader = ReaderOf(shape, rows);
        connection.EnqueueReaderResult(reader);
        await using var container = ctx.CreateSqlContainer("SELECT id, v FROM pg_shape");
        await using var tracked = await container.ExecuteReaderAsync();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var loaded = await DataReaderMapper.LoadAsync<Plain<T>>(tracked, new MapperOptions { Strict = true });
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        return new Run(loaded.Select(r => (object?)r.V).ToList(), bytes, reader.Calls);
    }

    private static readonly MethodInfo GatewayMethod =
        typeof(PostgreSqlProviderShapeHydrationTests).GetMethod(nameof(ThroughTheGateway), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo PlanMethod =
        typeof(PostgreSqlProviderShapeHydrationTests).GetMethod(nameof(ThroughThePlan), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Task<Run> Execute(MethodInfo open, Shape shape, DatabaseContext ctx, fakeDbConnection connection, int rows) =>
        (Task<Run>)open.MakeGenericMethod(shape.Property).Invoke(null, new object[] { ctx, connection, shape, rows })!;

    private static bool Same(object? a, object? b) =>
        a is Array x && b is Array y ? x.Cast<object>().SequenceEqual(y.Cast<object>()) : Equals(a, b);

    // Bytes to build what the shape's value is, which hydration has to build too.
    private static double ValueCost(Shape shape)
    {
        var keep = new object?[Rows];
        for (var i = 0; i < Rows; i++)
        {
            keep[i] = shape.Expected();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rows; i++)
        {
            keep[i] = shape.Expected();
        }

        GC.KeepAlive(keep);
        return (double)(GC.GetAllocatedBytesForCurrentThread() - before) / Rows;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task EveryShape_ReadsAsStored_ThroughBothMapperPaths(string name)
    {
        var shape = Of(name);
        using var ctx = Context(out var connection);

        foreach (var (path, open) in new[] { ("gateway", GatewayMethod), ("plan", PlanMethod) })
        {
            var run = await Execute(open, shape, ctx, connection, 3);
            Assert.True(run.Values.All(v => Same(v, shape.Expected())), $"{name}/{path} did not read back as stored");
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task EveryShape_CostsOneReaderCallPerColumn(string name)
    {
        var shape = Of(name);
        using var ctx = Context(out var connection);

        await Execute(GatewayMethod, shape, ctx, connection, 5); // builds and caches the plan
        var gateway = await Execute(GatewayMethod, shape, ctx, connection, Rows);
        await Execute(PlanMethod, shape, ctx, connection, 5);
        var plan = await Execute(PlanMethod, shape, ctx, connection, Rows);

        // Two columns (id, v) through the gateway, one (v) through the plan. A few calls per query (the schema) are
        // not per row, so the allowance is a handful in total, never one per row.
        const int perQuery = 8;
        Assert.True(gateway.Calls <= 2 * Rows + perQuery, $"{name}/gateway: {(double)gateway.Calls / Rows:F2} reader calls per row, expected 2");
        Assert.True(plan.Calls <= Rows + perQuery, $"{name}/plan: {(double)plan.Calls / Rows:F2} reader calls per row, expected 1");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task EveryShape_AllocatesLittleMoreThanItsValue(string name)
    {
        var shape = Of(name);
        using var ctx = Context(out var connection);
        var intShape = new Shape("int", () => 1, "integer", typeof(int), () => 1);

        await Execute(GatewayMethod, intShape, ctx, connection, 5);
        var intGateway = await Execute(GatewayMethod, intShape, ctx, connection, Rows);
        await Execute(PlanMethod, intShape, ctx, connection, 5);
        var intPlan = await Execute(PlanMethod, intShape, ctx, connection, Rows);
        await Execute(GatewayMethod, shape, ctx, connection, 5);
        var gateway = await Execute(GatewayMethod, shape, ctx, connection, Rows);
        await Execute(PlanMethod, shape, ctx, connection, 5);
        var plan = await Execute(PlanMethod, shape, ctx, connection, Rows);

        var value = ValueCost(shape);
        var gatewayExcess = (double)(gateway.Bytes - intGateway.Bytes) / Rows - value;
        var planExcess = (double)(plan.Bytes - intPlan.Bytes) / Rows - value;
        _output.WriteLine($"{name}: gateway {gatewayExcess:F0} B, plan {planExcess:F0} B beyond the entity and the value");

        // Boxing the provider value once is the allowance; the rest is the conversion's own cost.
        Assert.True(gatewayExcess <= 64, $"{name}/gateway allocates {gatewayExcess:F0} B per row beyond the value");
        Assert.True(planExcess <= 64, $"{name}/plan allocates {planExcess:F0} B per row beyond the value");
    }
}
