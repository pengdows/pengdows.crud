#region

using System.Data;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ColumnListCacheTests : SqlLiteContextTestBase
{
    [Fact]
    public void GetInsertableColumns_CachesList()
    {
        TypeMap.Register<CacheTestEntity>();
        var helper = new TableGateway<CacheTestEntity, int>(Context);
        var first = helper.GetCachedInsertableColumns();
        var second = helper.GetCachedInsertableColumns();
        Assert.Contains(first, c => c.Name == "Name");
        Assert.DoesNotContain(first, c => c.Name == "NoInsert" || c.Name == "Id");
        Assert.Same(first, second);
    }

    [Fact]
    public void GetInsertableColumns_NoInsertables_ReturnsEmpty()
    {
        TypeMap.Register<OnlyNonInsertEntity>();
        var helper = new TableGateway<OnlyNonInsertEntity, int>(Context);
        var first = helper.GetCachedInsertableColumns();
        var second = helper.GetCachedInsertableColumns();
        Assert.Empty(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetUpdatableColumns_CachesList()
    {
        TypeMap.Register<CacheTestEntity>();
        var helper = new TableGateway<CacheTestEntity, int>(Context);
        var first = helper.GetCachedUpdatableColumns();
        var second = helper.GetCachedUpdatableColumns();
        Assert.Contains(first, c => c.Name == "Name");
        Assert.Contains(first, c => c.Name == "NoInsert");
        Assert.DoesNotContain(first, c => c.Name == "Immutable" || c.Name == "Id" || c.Name == "Version");
        Assert.Same(first, second);
    }

    [Fact]
    public void GetUpdatableColumns_NoUpdatables_ReturnsEmpty()
    {
        TypeMap.Register<OnlyNonUpdateEntity>();
        var helper = new TableGateway<OnlyNonUpdateEntity, int>(Context);
        var first = helper.GetCachedUpdatableColumns();
        var second = helper.GetCachedUpdatableColumns();
        Assert.Empty(first);
        Assert.Same(first, second);
    }

    [Table("CacheTest")]
    private class CacheTestEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)] public string? Name { get; set; }

        [Column("Immutable", DbType.String)]
        [NonUpdateable]
        public string? Immutable { get; set; }

        [Column("NoInsert", DbType.String)]
        [NonInsertable]
        public string? NoInsert { get; set; }

        [Version]
        [Column("Version", DbType.Int32)]
        public int Version { get; set; }
    }

    [Table("OnlyNonInsert")]
    private class OnlyNonInsertEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        [NonInsertable]
        public int Id { get; set; }
    }

    [Table("OnlyNonUpdate")]
    private class OnlyNonUpdateEntity
    {
        [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }

        [Column("NoUpdate", DbType.String)]
        [NonUpdateable]
        public string? NoUpdate { get; set; }
    }
}
