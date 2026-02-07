#region

using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayTokenReplacementAdditionalTests : SqlLiteContextTestBase
{
    [Fact]
    public void ReplaceDialectTokens_UsesCustomMarkers()
    {
        var helper = new TableGateway<TokenEntity, long>(Context);
        var sql = "\"Users\".\"Name\" = ?p0 AND (\"Users\".\"Active\" = ?p1)";

        #pragma warning disable CS0618
        var replaced = helper.ReplaceDialectTokens(sql, "[", "]", "$");
        #pragma warning restore CS0618

        Assert.Contains("[Users].[Name]", replaced);
        Assert.Contains("?p0", replaced);
        Assert.Contains("?p1", replaced);
    }

    [Fact]
    public void ReplaceNeutralTokens_ReplacesAllTokens()
    {
        var helper = new TableGateway<TokenEntity, long>(Context);
        var sql = "{Q}Users{q} SET {S}first = 1, {S}second = 2";

        var replaced = helper.ReplaceNeutralTokens(sql);

        Assert.Contains("\"Users\"", replaced);
        Assert.Contains("@first", replaced);
        Assert.Contains("@second", replaced);
    }

    [Fact]
    public void ReplaceDialectTokens_NullSql_ThrowsArgumentNullException()
    {
        var helper = new TableGateway<TokenEntity, long>(Context);
        #pragma warning disable CS0618
        Assert.Throws<ArgumentNullException>(() => helper.ReplaceDialectTokens(null!, "[", "]", "$"));
        #pragma warning restore CS0618
    }

    [Table("token_entities")]
    private sealed class TokenEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }
}
