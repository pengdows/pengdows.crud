using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Moq;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Mapped column names by property name, for custom SQL: <c>gateway.ColumnName(nameof(Order.CustomerId))</c>
/// and <c>context.ColumnName&lt;Customer&gt;(nameof(Customer.Name))</c> return the <c>[Column]</c> name,
/// so a renamed column or property can't leave a hard-coded name behind. The caller wraps it with
/// <c>WrapObjectName</c>, which quotes for the container's own context.
/// </summary>
public class ColumnNameTests
{
    public class AuditedBase
    {
        [Column("created_on", DbType.DateTime)] public DateTime CreatedOn { get; set; }
    }

    [Table("orders")]
    public class Order : AuditedBase
    {
        [Id] [Column("order_id", DbType.Int64)] public long Id { get; set; }
        [Column("customer_id", DbType.Int64)] public long CustomerId { get; set; }
        public string? NotMapped { get; set; }
    }

    [Table("order_lines")]
    public class OrderLine
    {
        [PrimaryKey(1)] [Column("order_id", DbType.Int64)] public long OrderId { get; set; }
        [PrimaryKey(2)] [Column("line_no", DbType.Int32)] public int LineNo { get; set; }
        [Column("qty", DbType.Int32)] public int Quantity { get; set; }
    }

    [Table("customers")]
    public class Customer
    {
        [Id] [Column("customer_id", DbType.Int64)] public long Id { get; set; }
        [Column("display_name", DbType.String)] public string? Name { get; set; }
    }

    private static DatabaseContext Context() =>
        new("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));

    [Fact]
    public async Task TableGateway_ReturnsTheMappedColumn()
    {
        await using var context = Context();
        var gateway = new TableGateway<Order, long>(context);

        Assert.Equal("customer_id", gateway.ColumnName(nameof(Order.CustomerId)));
        Assert.Equal("order_id", gateway.ColumnName(nameof(Order.Id)));
    }

    [Fact]
    public async Task TableGateway_InheritedProperty_ReturnsItsColumn()
    {
        await using var context = Context();

        Assert.Equal("created_on", new TableGateway<Order, long>(context).ColumnName(nameof(Order.CreatedOn)));
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_ReturnsTheMappedColumn()
    {
        await using var context = Context();

        Assert.Equal("qty", new PrimaryKeyTableGateway<OrderLine>(context).ColumnName(nameof(OrderLine.Quantity)));
    }

    [Fact]
    public async Task Context_ReturnsAnyEntitysMappedColumn()
    {
        await using var context = Context();

        Assert.Equal("display_name", context.ColumnName<Customer>(nameof(Customer.Name)));
    }

    [Fact]
    public async Task TransactionContext_ReturnsTheMappedColumn()
    {
        await using var context = Context();
        await using var transaction = await context.BeginTransactionAsync();

        Assert.Equal("display_name", transaction.ColumnName<Customer>(nameof(Customer.Name)));
    }

    [Fact]
    public async Task ComposesWithWrapObjectName()
    {
        await using var context = Context();
        var gateway = new TableGateway<Order, long>(context);
        await using var sc = context.CreateSqlContainer();

        Assert.Equal("\"o\".\"customer_id\"", sc.WrapObjectName("o." + gateway.ColumnName(nameof(Order.CustomerId))));
    }

    [Fact]
    public async Task UnmappedProperty_ThrowsArgumentException_NamingEntityAndProperty()
    {
        await using var context = Context();
        var gateway = new TableGateway<Order, long>(context);

        var ex = Assert.Throws<ArgumentException>(() => gateway.ColumnName(nameof(Order.NotMapped)));
        Assert.Equal("propertyName", ex.ParamName);
        Assert.Contains("Order.NotMapped", ex.Message);
        Assert.Throws<ArgumentException>(() => context.ColumnName<Order>("NoSuchProperty"));
    }

    // C# property names are case-sensitive, and nameof always gives the exact name.
    [Fact]
    public async Task PropertyName_IsCaseSensitive()
    {
        await using var context = Context();

        Assert.Throws<ArgumentException>(() => new TableGateway<Order, long>(context).ColumnName("customerid"));
    }

    // A table info that isn't the registry's own TableInfo (no cached lookup) is scanned instead.
    [Fact]
    public void Resolver_OtherTableInfo_ScansTheColumns()
    {
        var column = new Mock<IColumnInfo>();
        column.SetupGet(c => c.Name).Returns("customer_id");
        column.SetupGet(c => c.PropertyInfo).Returns(typeof(Order).GetProperty(nameof(Order.CustomerId))!);
        var tableInfo = new Mock<ITableInfo>();
        tableInfo.SetupGet(t => t.Columns)
            .Returns(new System.Collections.Generic.Dictionary<string, IColumnInfo> { ["customer_id"] = column.Object });

        Assert.Equal("customer_id",
            pengdows.crud.@internal.ColumnNameResolver.Resolve(tableInfo.Object, typeof(Order), nameof(Order.CustomerId)));
        Assert.Throws<ArgumentException>(() =>
            pengdows.crud.@internal.ColumnNameResolver.Resolve(tableInfo.Object, typeof(Order), nameof(Order.Id)));
    }

    [Fact]
    public async Task NullPropertyName_Throws()
    {
        await using var context = Context();

        Assert.Throws<ArgumentNullException>(() => new TableGateway<Order, long>(context).ColumnName(null!));
        Assert.Throws<ArgumentNullException>(() => context.ColumnName<Order>(null!));
    }
}
