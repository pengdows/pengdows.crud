#region

using System.Data;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayRetrieveOneTests : SqlLiteContextTestBase
{
    [Fact]
    public void BuildRetrieve_IncludesPrimaryKeyWhereClause()
    {
        var helper = new TableGateway<RetrieveOneEntity, long>(Context);
        var entity = new RetrieveOneEntity { Id = 1, BusinessKey = "alpha" };
        var container = helper.BuildRetrieve(new[] { entity }, "alias");
        var sql = container.Query.ToString();

        Assert.Contains("WHERE", sql);
        Assert.Contains("@k0", sql);
        Assert.Contains("\"alias\".\"key_value\"", sql);
    }

    [Fact]
    public void BuildRetrieve_ReusesCachedQueryTemplate()
    {
        var helper = new TableGateway<RetrieveOneEntity, long>(Context);
        var entity = new RetrieveOneEntity { Id = 2, BusinessKey = "beta" };

        var first = helper.BuildRetrieve(new[] { entity }, "alias");
        var second = helper.BuildRetrieve(new[] { entity }, "alias");

        Assert.Equal(first.Query.ToString(), second.Query.ToString());
    }

    [Table("retrieve_one")]
    private sealed class RetrieveOneEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [PrimaryKey(1)]
        [Column("key_value", DbType.String)]
        public string BusinessKey { get; set; } = string.Empty;
    }
}
