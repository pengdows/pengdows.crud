#region

using System.Collections.Generic;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud;
using pengdows.crud.fakeDb;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayWhereClauseTests : SqlLiteContextTestBase
{
    public TableGatewayWhereClauseTests()
    {
        TypeMap.Register<WhereEntity>();
        TypeMap.Register<NullableWhereEntity>();
    }

    [Fact]
    public void BuildWhere_NullIds_DoesNotModifyQuery()
    {
        var helper = new TableGateway<WhereEntity, long>(Context);
        using var container = helper.BuildBaseRetrieve(string.Empty, Context);
        var original = container.Query.ToString();
        var wrapped = container.WrapObjectName("id");

        IEnumerable<long>? ids = null;
        #pragma warning disable CS8604
        helper.BuildWhere(wrapped, ids!, container);
        #pragma warning restore CS8604

        Assert.Equal(original, container.Query.ToString());
    }

    [Fact]
    public void BuildWhere_OnlyNullIds_AppendsIsNull()
    {
        var helper = new TableGateway<NullableWhereEntity, long?>(Context);
        using var container = helper.BuildBaseRetrieve(string.Empty, Context);
        var wrapped = container.WrapObjectName("id");

        helper.BuildWhere(wrapped, new long?[] { null, null }, container);

        Assert.Contains("IS NULL", container.Query.ToString());
    }

    [Fact]
    public void BuildWhere_SingleValueWithNull_IncludesOrIsNull()
    {
        var helper = new TableGateway<NullableWhereEntity, long?>(Context);
        using var container = helper.BuildBaseRetrieve(string.Empty, Context);
        var wrapped = container.WrapObjectName("id");

        helper.BuildWhere(wrapped, new long?[] { 42, null }, container);

        var sql = container.Query.ToString();
        Assert.Contains("OR", sql);
        Assert.Contains("IS NULL", sql);
    }

    [Fact]
    public void BuildWhere_WithSetValuedParameters_UsesAnyClause()
    {
        using var context = CreatePostgresContext();
        var helper = new TableGateway<WhereEntity, long>(context);
        using var container = helper.BuildBaseRetrieve(string.Empty, context);
        var wrapped = container.WrapObjectName("id");

        helper.BuildWhere(wrapped, new[] { 1L, 2L, 3L }, container);

        var sql = container.Query.ToString();
        Assert.Contains("ANY", sql);
    }

    private static DatabaseContext CreatePostgresContext()
    {
        var map = new TypeMapRegistry();
        map.Register<WhereEntity>();
        map.Register<NullableWhereEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        return new DatabaseContext("Data Source=:memory:;EmulatedProduct=PostgreSql", factory, map);
    }

    [Table("where_entities")]
    private sealed class WhereEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }

    [Table("nullable_where_entities")]
    private sealed class NullableWhereEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long? Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }
}
