using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// HARN-013: the batch, upsert and create paths read back what they wrote. The earlier batch tests only asserted that a row count
/// was non-negative, so a batch that wrote the wrong columns, or the right columns to the wrong rows, still passed.
/// </summary>
[Collection("SqliteSerial")]
public sealed class TableGatewayBatchRoundTripTests
{
    [Table("rt_rows")]
    public sealed class Row
    {
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Column("age", DbType.Int32)] public int Age { get; set; }
        [Column("salary", DbType.Double)] public double Salary { get; set; }
    }

    private static (DatabaseContext Context, TableGateway<Row, int> Gateway) Create()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { EnableDataPersistence = true };
        factory.DataStore.DefineUniqueKey("rt_rows", "id");
        var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory, new TypeMapRegistry());
        return (context, new TableGateway<Row, int>(context));
    }

    private static async Task<List<Row>> All(TableGateway<Row, int> gateway)
    {
        await using var sc = gateway.BuildBaseRetrieve("r");
        return (await gateway.LoadListAsync(sc)).OrderBy(r => r.Id).ToList();
    }

    private static Row[] Rows(int from, int count, string prefix) =>
        Enumerable.Range(from, count)
            .Select(i => new Row { Id = i, Name = $"{prefix}{i}", Age = 20 + i, Salary = 1000 + i })
            .ToArray();

    [Fact]
    public async Task BatchCreate_WritesEveryRowWithEveryColumn()
    {
        var (context, gateway) = Create();
        await using var _ = context;

        var written = await gateway.BatchCreateAsync(Rows(1, 5, "n"));

        Assert.Equal(5, written);
        var stored = await All(gateway);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, stored.Select(r => r.Id));
        Assert.Equal(new[] { "n1", "n2", "n3", "n4", "n5" }, stored.Select(r => r.Name));
        Assert.Equal(new[] { 21, 22, 23, 24, 25 }, stored.Select(r => r.Age));
        Assert.Equal(new[] { 1001d, 1002d, 1003d, 1004d, 1005d }, stored.Select(r => r.Salary));
    }

    [Fact]
    public async Task CreateList_AndASingleEntityBatch_ReadBackToo()
    {
        var (context, gateway) = Create();
        await using var _ = context;

        await gateway.CreateAsync((IReadOnlyList<Row>)Rows(1, 2, "list"));
        await gateway.BatchCreateAsync(new[] { new Row { Id = 9, Name = "solo", Age = 99, Salary = 9.5 } });

        var stored = await All(gateway);
        Assert.Equal(new[] { 1, 2, 9 }, stored.Select(r => r.Id));
        Assert.Equal(new[] { "list1", "list2", "solo" }, stored.Select(r => r.Name));
        Assert.Equal(99, stored.Single(r => r.Id == 9).Age);
        Assert.Equal(9.5, stored.Single(r => r.Id == 9).Salary);
    }

    [Fact]
    public async Task BatchUpdate_ChangesTheNamedRowsOnly()
    {
        var (context, gateway) = Create();
        await using var _ = context;
        await gateway.BatchCreateAsync(Rows(1, 5, "n"));

        var updated = await gateway.BatchUpdateAsync(new[]
        {
            new Row { Id = 2, Name = "changed2", Age = 72, Salary = 7002 },
            new Row { Id = 4, Name = "changed4", Age = 74, Salary = 7004 }
        });

        Assert.Equal(2, updated);
        var stored = await All(gateway);
        Assert.Equal(new[] { "n1", "changed2", "n3", "changed4", "n5" }, stored.Select(r => r.Name));
        Assert.Equal(new[] { 21, 72, 23, 74, 25 }, stored.Select(r => r.Age));
        Assert.Equal(new[] { 1001d, 7002d, 1003d, 7004d, 1005d }, stored.Select(r => r.Salary));
    }

    [Fact]
    public async Task BatchUpsert_UpdatesExistingRowsAndInsertsNewOnes()
    {
        var (context, gateway) = Create();
        await using var _ = context;
        await gateway.BatchCreateAsync(Rows(1, 3, "n"));

        await gateway.BatchUpsertAsync(new[]
        {
            new Row { Id = 2, Name = "upserted2", Age = 82, Salary = 8002 },
            new Row { Id = 7, Name = "inserted7", Age = 87, Salary = 8007 }
        });

        var stored = await All(gateway);
        Assert.Equal(new[] { 1, 2, 3, 7 }, stored.Select(r => r.Id));
        Assert.Equal(new[] { "n1", "upserted2", "n3", "inserted7" }, stored.Select(r => r.Name));
        Assert.Equal(new[] { 21, 82, 23, 87 }, stored.Select(r => r.Age));
        Assert.Equal(new[] { 1001d, 8002d, 1003d, 8007d }, stored.Select(r => r.Salary));
    }
}
