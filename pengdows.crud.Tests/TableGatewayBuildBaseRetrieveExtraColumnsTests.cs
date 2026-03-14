using System;
using System.Data;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

public class TableGatewayBuildBaseRetrieveExtraColumnsTests : SqlLiteContextTestBase
{
    [Table("bbr_entity")]
    private sealed class BbrEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void BuildBaseRetrieve_WithExtraSelectExpressions_InjectsBeforeFrom()
    {
        TypeMap.Register<BbrEntity>();
        var gateway = new TableGateway<BbrEntity, int>(Context);

        var sc = gateway.BuildBaseRetrieve("t", new[] { "tr.object_id" });
        var sql = sc.Query.ToString();

        var fromIdx = sql.IndexOf("\nFROM", StringComparison.Ordinal);
        Assert.True(fromIdx > 0, "Expected \\nFROM in generated SQL");

        var selectPart = sql[..fromIdx];
        Assert.Contains("object_id", selectPart);
        Assert.DoesNotContain("object_id", sql[fromIdx..]);
    }

    [Fact]
    public void BuildBaseRetrieve_WithMultipleExtraSelectExpressions_AllAppearBeforeFrom()
    {
        TypeMap.Register<BbrEntity>();
        var gateway = new TableGateway<BbrEntity, int>(Context);

        var sc = gateway.BuildBaseRetrieve("t", new[] { "tr.object_id", "tt.taxonomy" });
        var sql = sc.Query.ToString();

        var fromIdx = sql.IndexOf("\nFROM", StringComparison.Ordinal);
        Assert.True(fromIdx > 0);

        var selectPart = sql[..fromIdx];
        Assert.Contains("object_id", selectPart);
        Assert.Contains("taxonomy", selectPart);
    }

    [Fact]
    public void BuildBaseRetrieve_WithEmptyExtraSelectExpressions_ProducesSameAsBaseOverload()
    {
        TypeMap.Register<BbrEntity>();
        var gateway = new TableGateway<BbrEntity, int>(Context);

        var baseline = gateway.BuildBaseRetrieve("t").Query.ToString();
        var withEmpty = gateway.BuildBaseRetrieve("t", Array.Empty<string>()).Query.ToString();

        Assert.Equal(baseline, withEmpty);
    }
}
