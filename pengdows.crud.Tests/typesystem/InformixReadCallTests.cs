using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

// Informix's driver has two quirks the read has to honor: it reports an out-of-range DECIMAL as a null GetValue, and it
// returns LIST/SET/MULTISET columns as literal text. Both reads are one call into the reader per column, not a check
// followed by the read.
[Collection("TypeRegistry")]
public class InformixReadCallTests
{
    [Table("informix_read")]
    public sealed class DecimalRow
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Decimal)] public decimal V { get; set; }
    }

    [Table("informix_read_list")]
    public sealed class ListRow
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Object)] public int[]? V { get; set; }
    }

    public sealed class PlainList
    {
        public int[]? V { get; set; }
    }

    private sealed class CountingReader : fakeDbDataReader
    {
        private int _depth;

        public CountingReader(IEnumerable<Dictionary<string, object>> rows) : base(rows)
        {
        }

        public int Calls { get; private set; }

        private T Hit<T>(Func<T> read)
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
        public override decimal GetDecimal(int o) => Hit(() => base.GetDecimal(o));
        public override T GetFieldValue<T>(int o) => Hit(() => base.GetFieldValue<T>(o));
    }

    private const int Rows = 200;

    private static DatabaseContext Informix(out fakeDbConnection connection)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Informix);
        connection = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Informix,
            ConnectionString = "Server=x;Database=d;EmulatedProduct=Informix"
        };
        factory.Connections.Add(connection);
        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;Database=d;EmulatedProduct=Informix",
            DbMode = DbMode.SingleConnection
        }, factory, NullLoggerFactory.Instance);
    }

    private static CountingReader ReaderOf(object value, Type column, int rows)
    {
        var data = new List<Dictionary<string, object>>();
        for (var i = 0; i < rows; i++)
        {
            data.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["id"] = i, ["v"] = value });
        }

        return new CountingReader(data)
        {
            Columns = [new fakeDbColumn("id", typeof(int), "INTEGER"), new fakeDbColumn("v", column, "X")]
        };
    }

    // The calls a second pass makes for Rows rows, after the plan is built.
    private static async Task<int> GatewayCalls<TRow>(object value, Type column) where TRow : class, new()
    {
        using var ctx = Informix(out var connection);
        var gateway = new TableGateway<TRow, int>(ctx);
        connection.EnqueueReaderResult(ReaderOf(value, column, 3));
        await using (var warm = ctx.CreateSqlContainer("SELECT id, v FROM t"))
        {
            await gateway.LoadListAsync(warm);
        }

        var reader = ReaderOf(value, column, Rows);
        connection.EnqueueReaderResult(reader);
        await using var container = ctx.CreateSqlContainer("SELECT id, v FROM t");
        var loaded = await gateway.LoadListAsync(container);
        Assert.Equal(Rows, loaded.Count);
        return reader.Calls;
    }

    private static async Task<int> PlanCalls<T>(object value, Type column) where T : class, new()
    {
        using var ctx = Informix(out var connection);
        connection.EnqueueReaderResult(ReaderOf(value, column, 3));
        await using (var warmContainer = ctx.CreateSqlContainer("SELECT id, v FROM t"))
        await using (var warmReader = await warmContainer.ExecuteReaderAsync())
        {
            await DataReaderMapper.LoadAsync<T>(warmReader, new MapperOptions { Strict = true });
        }

        var reader = ReaderOf(value, column, Rows);
        connection.EnqueueReaderResult(reader);
        await using var container = ctx.CreateSqlContainer("SELECT id, v FROM t");
        await using var tracked = await container.ExecuteReaderAsync();
        var loaded = await DataReaderMapper.LoadAsync<T>(tracked, new MapperOptions { Strict = true });
        Assert.Equal(Rows, loaded.Count);
        return reader.Calls;
    }

    private const int PerQuery = 8;

    [Fact]
    public async Task Decimal_ThroughTheGateway_IsOneCallPerColumn()
    {
        var calls = await GatewayCalls<DecimalRow>(12.5m, typeof(decimal));

        Assert.True(calls <= 2 * Rows + PerQuery, $"{(double)calls / Rows:F2} reader calls per row, expected 2 (id, v)");
    }

    [Fact]
    public async Task CollectionLiteral_ThroughTheGateway_IsOneCallPerColumn()
    {
        var calls = await GatewayCalls<ListRow>("LIST{1          ,2          }", typeof(string));

        Assert.True(calls <= 2 * Rows + PerQuery, $"{(double)calls / Rows:F2} reader calls per row, expected 2 (id, v)");
    }

    [Fact]
    public async Task CollectionLiteral_ThroughThePlan_IsOneCallPerColumn()
    {
        var calls = await PlanCalls<PlainList>("LIST{1          ,2          }", typeof(string));

        Assert.True(calls <= Rows + PerQuery, $"{(double)calls / Rows:F2} reader calls per row, expected 1");
    }

    [Fact]
    public async Task CollectionLiteral_ReadsTheSameValuesAsBefore()
    {
        using var ctx = Informix(out var connection);
        connection.EnqueueReaderResult(ReaderOf("LIST{1          ,2          }", typeof(string), 1));
        await using var container = ctx.CreateSqlContainer("SELECT id, v FROM t");
        var rows = await new TableGateway<ListRow, int>(ctx).LoadListAsync(container);

        Assert.Equal(new[] { 1, 2 }, Assert.Single(rows).V);
    }
}
