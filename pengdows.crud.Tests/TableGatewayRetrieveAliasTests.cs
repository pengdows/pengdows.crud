#region

using System.Data;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayRetrieveAliasTests : SqlLiteContextTestBase
{
    [Fact]
    public void BuildRetrieve_SingleId_WithAlias_QualifiesIdColumnInWhereClause()
    {
        TypeMap.Register<AliasEntity>();
        var helper = new TableGateway<AliasEntity, int>(Context);

        var sc = helper.BuildRetrieve(new[] { 1 }, "alias");
        var sql = sc.Query.ToString();

        var dialect = Context.GetDialect();
        var expected =
            " WHERE " +
            dialect.WrapSimpleName("alias") +
            dialect.CompositeIdentifierSeparator +
            dialect.WrapSimpleName("id") +
            " = " +
            dialect.MakeParameterName("p0");

        Assert.Contains(expected, sql);
    }

    [Table("alias_entity")]
    private sealed class AliasEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }
}
