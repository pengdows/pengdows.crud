using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-010: Npgsql on Spanner refuses GetValue for uuid columns and for value-type arrays (confirmed
/// live 2026-10-03: GetValue throws even for an int8[] with no nulls; GetFieldValue&lt;long?[]&gt;
/// works). Each row used to throw and catch that refusal; each plan column now learns it once (one
/// refusal for the uuid and one for the array, whatever the row count). InterBase also reports its
/// arrays as System.Array but returns them through GetValue, so the field type can't decide alone.
/// </summary>
public sealed class SpannerPerRowReadTests
{
    private const int Rows = 50;

    [Table("sp_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("u", DbType.Guid)] public System.Guid U { get; set; }
        [Column("a", DbType.Object)] public long[]? A { get; set; }
    }

    private static (DatabaseContext Context, fakeDbConnection Exec) Context()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Spanner);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Spanner });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Spanner };
        factory.Connections.Add(exec);
        return (new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Host=x;EmulatedProduct=Spanner",
            DbMode = DbMode.Standard
        }, factory), exec);
    }

    private static fakeDbDataReader Reader() =>
        new(Enumerable.Range(1, Rows).Select(i => new Dictionary<string, object>
        {
            ["id"] = (long)i,
            ["u"] = System.Guid.NewGuid().ToByteArray(bigEndian: true),
            ["a"] = new long?[] { i, i + 1 }
        }).ToArray())
        {
            UnknownTypeColumns = new HashSet<string> { "u" },
            NullableElementArrayColumns = new HashSet<string> { "a" }
        };

    [Fact]
    public async Task Gateway_ManyRows_RefusalIsNotThrownPerRow()
    {
        var (context, exec) = Context();
        await using var _ = context;
        var reader = Reader();
        exec.EnqueueReaderResult(reader);
        var gateway = new TableGateway<Row, long>(context);
        await using var sc = gateway.BuildBaseRetrieve("r");

        var rows = await gateway.LoadListAsync(sc);

        Assert.Equal(Rows, rows.Count);
        Assert.Equal(new long[] { Rows, Rows + 1 }, rows[^1].A);
        Assert.InRange(reader.GetValueExceptionCount, 0, 2);
    }

    [Fact]
    public async Task DataReaderMapper_ManyRows_RefusalIsNotThrownPerRow()
    {
        var (context, exec) = Context();
        await using var _ = context;
        var reader = Reader();
        exec.EnqueueReaderResult(reader);
        await using var sc = context.CreateSqlContainer("SELECT id, u, a FROM sp_rows");
        await using var tracked = await sc.ExecuteReaderAsync();

        var rows = await DataReaderMapper.LoadAsync<Row>(tracked, new MapperOptions(Strict: true, ColumnsOnly: true));

        Assert.Equal(Rows, rows.Count);
        Assert.Equal(new long[] { Rows, Rows + 1 }, rows[^1].A);
        Assert.InRange(reader.GetValueExceptionCount, 0, 2);
    }
}
