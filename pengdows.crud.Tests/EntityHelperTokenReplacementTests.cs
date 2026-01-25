#region

using System;
using System.Data;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperTokenReplacementTests : SqlLiteContextTestBase
{
    [Table("Tokens")]
    private class TokenEntity
    {
        [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }
    }

    [Fact]
    public void ReplaceDialectTokens_ReplacesMarkers()
    {
        TypeMap.Register<TokenEntity>();
        var helper = new EntityHelper<TokenEntity, int>(Context);
        var entity = new TokenEntity { Id = 1 };
        var sc = helper.BuildCreate(entity);
        var sql = sc.Query.ToString();
#pragma warning disable CS0618 // Type or member is obsolete
        var replaced = helper.ReplaceDialectTokens(sql, "[", "]", ":");
#pragma warning restore CS0618 // Type or member is obsolete
        Assert.Equal("INSERT INTO [Tokens] ([Id]) VALUES (:i0)", replaced);
    }

    [Fact]
    public void ReplaceDialectTokens_NullSql_Throws()
    {
        var helper = new EntityHelper<TokenEntity, int>(Context);
#pragma warning disable CS0618 // Type or member is obsolete
        Assert.Throws<ArgumentNullException>(() => helper.ReplaceDialectTokens(null!, "[", "]", ":"));
#pragma warning restore CS0618 // Type or member is obsolete
    }
}