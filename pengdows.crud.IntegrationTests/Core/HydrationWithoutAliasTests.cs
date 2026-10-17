using System.Data;
using Microsoft.Data.Sqlite;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Custom SQL hydrates into entities by the declared [Column] names, so the query needs no
/// "AS PropertyName" aliases. Real SQLite engine, no container.
/// </summary>
public sealed class HydrationWithoutAliasTests : IAsyncLifetime
{
    private readonly string _file = Path.Combine(Directory.CreateTempSubdirectory("pengdows-hyd-").FullName, "h.sqlite");
    private DatabaseContext? _ctx;

    [Table("hyd_orders")]
    public class OrderRow
    {
        [PrimaryKey(1)][Column("order_id", DbType.Int64)] public long OrderId { get; set; }
        [Column("customer_name", DbType.String)] public string CustomerName { get; set; } = "";
        [Column("total_amount", DbType.Decimal)] public decimal TotalAmount { get; set; }
    }

    public async Task InitializeAsync()
    {
        _ctx = new DatabaseContext($"Data Source={_file}", SqliteFactory.Instance);
        await using (var ddl = _ctx.CreateSqlContainer(
            "CREATE TABLE hyd_orders (order_id INTEGER PRIMARY KEY, customer_name TEXT NOT NULL, total_amount NUMERIC NOT NULL)"))
        {
            await ddl.ExecuteNonQueryAsync();
        }

        var gw = new PrimaryKeyTableGateway<OrderRow>(_ctx);
        await gw.CreateAsync(new OrderRow { OrderId = 1, CustomerName = "ada", TotalAmount = 12.5m }, _ctx);
        await gw.CreateAsync(new OrderRow { OrderId = 2, CustomerName = "lin", TotalAmount = 3m }, _ctx);
    }

    public Task DisposeAsync()
    {
        _ctx?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SelectStar_Hydrates_WithoutAliases()
    {
        var gw = new PrimaryKeyTableGateway<OrderRow>(_ctx!);
        await using var sc = _ctx!.CreateSqlContainer("SELECT * FROM hyd_orders ORDER BY order_id");

        var rows = await gw.LoadListAsync(sc);

        Assert.Equal(2, rows.Count);
        Assert.Equal((1L, "ada", 12.5m), (rows[0].OrderId, rows[0].CustomerName, rows[0].TotalAmount));
    }

    [Fact]
    public async Task ColumnSubsetAndFilter_Hydrates_WithoutAliases()
    {
        var gw = new PrimaryKeyTableGateway<OrderRow>(_ctx!);
        await using var sc = _ctx!.CreateSqlContainer("SELECT order_id, customer_name FROM hyd_orders WHERE total_amount > ");
        sc.Query.Append(sc.MakeParameterName(sc.AddParameterWithValue("min", DbType.Decimal, 5m)));

        var rows = await gw.LoadListAsync(sc);

        var only = Assert.Single(rows);
        Assert.Equal("ada", only.CustomerName);
        Assert.Equal(0m, only.TotalAmount); // not selected, left at default
    }

    [Fact]
    public async Task ColumnNameCase_DoesNotMatter()
    {
        var gw = new PrimaryKeyTableGateway<OrderRow>(_ctx!);
        await using var sc = _ctx!.CreateSqlContainer("SELECT order_id AS ORDER_ID, customer_name AS CUSTOMER_NAME FROM hyd_orders WHERE order_id = 2");

        var rows = await gw.LoadListAsync(sc);

        Assert.Equal("lin", Assert.Single(rows).CustomerName);
    }
}
