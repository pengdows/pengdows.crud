#region
using System;
using System.Data;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperOrderingTests : SqlLiteContextTestBase
{
    [Fact]
    public void BuildBaseRetrieve_OrdersColumnsByOrdinal()
    {
        TypeMap.Register<OrderedEntity>();
        var helper = new EntityHelper<OrderedEntity, int>(Context);
        var sc = helper.BuildBaseRetrieve(string.Empty);
        var query = sc.Query.ToString();
        Assert.Contains("SELECT \"A\", \"B\"", query);
    }

    [Fact]
    public void BuildBaseRetrieve_DefaultsToPropertyOrderWithoutOrdinals()
    {
        TypeMap.Register<DefaultEntity>();
        var helper = new EntityHelper<DefaultEntity, int>(Context);
        var sc = helper.BuildBaseRetrieve(string.Empty);
        var query = sc.Query.ToString();
        Assert.Contains("SELECT \"B\", \"A\"", query);
    }

    [Fact]
    public void BuildWhereByPrimaryKey_OrdersByPkOrder()
    {
        TypeMap.Register<OrderedEntity>();
        var helper = new EntityHelper<OrderedEntity, int>(Context);
        var sc = Context.CreateSqlContainer();
        helper.BuildWhereByPrimaryKey(new[] { new OrderedEntity { A = 1, B = 2 } }, sc);
        var query = sc.Query.ToString();
        Assert.Contains("(\"A\" = @p0 AND \"B\" = @p1)", query);
    }

    [Fact]
    public void BuildWhereByPrimaryKey_NoKeys_Throws()
    {
        TypeMap.Register<DefaultEntity>();
        var helper = new EntityHelper<DefaultEntity, int>(Context);
        var sc = Context.CreateSqlContainer();
        Assert.Throws<Exception>(() => helper.BuildWhereByPrimaryKey(new[] { new DefaultEntity { A = 1, B = 2 } }, sc));
    }

    [Table("Ordered")]
    private class OrderedEntity
    {
        [PrimaryKey(1)]
        [Column("A", DbType.Int32, 1)]
        public int A { get; set; }

        [PrimaryKey(2)]
        [Column("B", DbType.Int32, 2)]
        public int B { get; set; }
    }

    [Table("Default")]
    private class DefaultEntity
    {
        [Column("B", DbType.Int32)]
        public int B { get; set; }

        [Column("A", DbType.Int32)]
        public int A { get; set; }
    }
}

