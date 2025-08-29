#region

using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperTokenReplacementTests : SqlLiteContextTestBase
{
    [Table("Tokens")]
    private class TokenEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }
    }

    [Fact]
    public void ReplaceDialectTokens_ReplacesMarkers()
    {
        TypeMap.Register<TokenEntity>();
        var helper = new EntityHelper<TokenEntity, int>(Context);
        var entity = new TokenEntity { Id = 1 };
        var sc = helper.BuildCreate(entity);
        var sql = sc.Query.ToString();
        var replaced = helper.ReplaceDialectTokens(sql, "[", "]", ":");
        Assert.Equal("INSERT INTO [Tokens] ([Id]) VALUES (:p0)", replaced);
    }

    [Fact]
    public void ReplaceDialectTokens_NullSql_Throws()
    {
        var helper = new EntityHelper<TokenEntity, int>(Context);
        Assert.Throws<ArgumentNullException>(() => helper.ReplaceDialectTokens(null!, "[", "]", ":"));
    }
}

