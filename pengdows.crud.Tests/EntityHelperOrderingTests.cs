#region

using System;
using System.Data;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayOrderingTests : SqlLiteContextTestBase
{
    [Fact]
    public void BuildBaseRetrieve_ExactSql_NoAlias()
    {
        TypeMap.Register<OrderedEntity>();
        var helper = new TableGateway<OrderedEntity, int>(Context);
        var sc = helper.BuildBaseRetrieve(string.Empty);
        var wrappedA = Context.WrapObjectName("A");
        var wrappedB = Context.WrapObjectName("B");
        var wrappedTable = Context.WrapObjectName("Ordered");
        var expected = $"SELECT {wrappedA}, {wrappedB}\nFROM {wrappedTable}";
        Assert.Equal(expected, sc.Query.ToString());
    }

    [Fact]
    public void BuildBaseRetrieve_ExactSql_WithAlias()
    {
        TypeMap.Register<OrderedEntity>();
        var helper = new TableGateway<OrderedEntity, int>(Context);
        var alias = "a";
        var sc = helper.BuildBaseRetrieve(alias);
        var wrappedAlias = Context.WrapObjectName(alias);
        var wrappedA = Context.WrapObjectName("A");
        var wrappedB = Context.WrapObjectName("B");
        var wrappedTable = Context.WrapObjectName("Ordered");
        var expected =
            $"SELECT {wrappedAlias}.{wrappedA}, {wrappedAlias}.{wrappedB}\nFROM {wrappedTable} {wrappedAlias}";
        Assert.Equal(expected, sc.Query.ToString());
    }

    [Fact]
    public void BuildBaseRetrieve_OrdersColumnsByOrdinal()
    {
        TypeMap.Register<OrderedEntity>();
        var helper = new TableGateway<OrderedEntity, int>(Context);
        var sc = helper.BuildBaseRetrieve(string.Empty);
        var query = sc.Query.ToString();
        Assert.Contains("SELECT \"A\", \"B\"", query);
    }

    [Fact]
    public void BuildBaseRetrieve_DefaultsToPropertyOrderWithoutOrdinals()
    {
        TypeMap.Register<DefaultEntity>();
        var helper = new TableGateway<DefaultEntity, int>(Context);
        var sc = helper.BuildBaseRetrieve(string.Empty);
        var query = sc.Query.ToString();
        Assert.Contains("SELECT \"B\", \"A\"", query);
    }

    [Fact]
    public void BuildWhereByPrimaryKey_OrdersByPkOrder()
    {
        TypeMap.Register<OrderedEntity>();
        var helper = new TableGateway<OrderedEntity, int>(Context);
        var sc = Context.CreateSqlContainer();
        helper.BuildWhereByPrimaryKey(new[] { new OrderedEntity { A = 1, B = 2 } }, sc);
        var query = sc.Query.ToString();
        // Check that the columns are ordered by PK order (A before B) but allow any parameter names
        Assert.Contains("(\"A\" = @", query);
        Assert.Contains(" AND \"B\" = @", query);
        // Verify A comes before B in the query
        var aIndex = query.IndexOf("\"A\" = @", StringComparison.Ordinal);
        var bIndex = query.IndexOf("\"B\" = @", StringComparison.Ordinal);
        Assert.True(aIndex < bIndex, "Primary key column A should appear before B in the query");
    }

    [Fact]
    public void BuildWhereByPrimaryKey_ExactSql_SingleCompositeKey()
    {
        TypeMap.Register<OrderedEntity>();
        var helper = new TableGateway<OrderedEntity, int>(Context);
        var sc = Context.CreateSqlContainer();
        helper.BuildWhereByPrimaryKey(new[] { new OrderedEntity { A = 1, B = 2 } }, sc);
        var expected = "\n WHERE (\"A\" = @k0 AND \"B\" = @k1)";
        Assert.Equal(expected, sc.Query.ToString());
    }

    [Fact]
    public void BuildWhereByPrimaryKey_ExactSql_MultipleCompositeKeys()
    {
        TypeMap.Register<OrderedEntity>();
        var helper = new TableGateway<OrderedEntity, int>(Context);
        var sc = Context.CreateSqlContainer();
        var list = new[]
        {
            new OrderedEntity { A = 1, B = 2 },
            new OrderedEntity { A = 3, B = 4 }
        };
        helper.BuildWhereByPrimaryKey(list, sc);
        var expected = "\n WHERE (\"A\" = @k0 AND \"B\" = @k1) OR (\"A\" = @k2 AND \"B\" = @k3)";
        Assert.Equal(expected, sc.Query.ToString());
    }

    [Fact]
    public void BuildWhereByPrimaryKey_MultipleCompositeKeys_GeneratesOr()
    {
        TypeMap.Register<OrderedEntity>();
        var helper = new TableGateway<OrderedEntity, int>(Context);
        var sc = Context.CreateSqlContainer();
        var list = new[]
        {
            new OrderedEntity { A = 1, B = 2 },
            new OrderedEntity { A = 3, B = 4 }
        };
        helper.BuildWhereByPrimaryKey(list, sc);
        var query = sc.Query.ToString();
        Assert.Contains(" OR ", query);
    }

    [Fact]
    public void BuildWhereByPrimaryKey_NoKeys_Throws()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.Register<NoKeyEntity>());
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
        [Id] [Column("B", DbType.Int32)] public int B { get; set; }

        [Column("A", DbType.Int32)] public int A { get; set; }
    }

    [Table("NoKey")]
    private class NoKeyEntity
    {
        [Column("A", DbType.Int32)] public int A { get; set; }

        [Column("B", DbType.Int32)] public int B { get; set; }
    }
}