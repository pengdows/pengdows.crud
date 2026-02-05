#region

using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayTokenReplacementTests : SqlLiteContextTestBase
{
    [Table("Tokens")]
    private class TokenEntity
    {
        [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }
    }

    [Fact]
    public void ReplaceNeutralTokens_ReplacesMarkers()
    {
        TypeMap.Register<TokenEntity>();
        var helper = new TableGateway<TokenEntity, int>(Context);
        var dialect = ((ISqlDialectProvider)Context).Dialect;
        var replaced = helper.ReplaceNeutralTokens("INSERT INTO {Q}Tokens{q} ({Q}Id{q}) VALUES ({S}i0)");
        var expected = $"INSERT INTO {dialect.WrapObjectName("Tokens")} ({dialect.WrapObjectName("Id")}) VALUES ({dialect.ParameterMarker}i0)";
        Assert.Equal(expected, replaced);
    }

    [Fact]
    public void ReplaceNeutralTokens_NullSql_Throws()
    {
        var helper = new TableGateway<TokenEntity, int>(Context);
        Assert.Throws<ArgumentNullException>(() => helper.ReplaceNeutralTokens(null!));
    }
}
