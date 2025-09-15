#region

using System.Collections.Generic;
using System.Data;
using System.Reflection;
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
        var helper = new EntityHelper<CacheTestEntity, int>(Context);
        var method = typeof(EntityHelper<CacheTestEntity, int>).GetMethod("GetCachedInsertableColumns", BindingFlags.NonPublic | BindingFlags.Instance);
        var first = (IReadOnlyList<IColumnInfo>)method!.Invoke(helper, null)!;
        var second = (IReadOnlyList<IColumnInfo>)method.Invoke(helper, null)!;
        Assert.Contains(first, c => c.Name == "Name");
        Assert.DoesNotContain(first, c => c.Name == "NoInsert" || c.Name == "Id");
        Assert.Same(first, second);
    }

    [Fact]
    public void GetInsertableColumns_NoInsertables_ReturnsEmpty()
    {
        TypeMap.Register<OnlyNonInsertEntity>();
        var helper = new EntityHelper<OnlyNonInsertEntity, int>(Context);
        var method = typeof(EntityHelper<OnlyNonInsertEntity, int>).GetMethod("GetCachedInsertableColumns", BindingFlags.NonPublic | BindingFlags.Instance);
        var first = (IReadOnlyList<IColumnInfo>)method!.Invoke(helper, null)!;
        var second = (IReadOnlyList<IColumnInfo>)method.Invoke(helper, null)!;
        Assert.Empty(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetUpdatableColumns_CachesList()
    {
        TypeMap.Register<CacheTestEntity>();
        var helper = new EntityHelper<CacheTestEntity, int>(Context);
        var method = typeof(EntityHelper<CacheTestEntity, int>).GetMethod("GetCachedUpdatableColumns", BindingFlags.NonPublic | BindingFlags.Instance);
        var first = (IReadOnlyList<IColumnInfo>)method!.Invoke(helper, null)!;
        var second = (IReadOnlyList<IColumnInfo>)method.Invoke(helper, null)!;
        Assert.Contains(first, c => c.Name == "Name");
        Assert.Contains(first, c => c.Name == "NoInsert");
        Assert.DoesNotContain(first, c => c.Name == "Immutable" || c.Name == "Id" || c.Name == "Version");
        Assert.Same(first, second);
    }

    [Fact]
    public void GetUpdatableColumns_NoUpdatables_ReturnsEmpty()
    {
        TypeMap.Register<OnlyNonUpdateEntity>();
        var helper = new EntityHelper<OnlyNonUpdateEntity, int>(Context);
        var method = typeof(EntityHelper<OnlyNonUpdateEntity, int>).GetMethod("GetCachedUpdatableColumns", BindingFlags.NonPublic | BindingFlags.Instance);
        var first = (IReadOnlyList<IColumnInfo>)method!.Invoke(helper, null)!;
        var second = (IReadOnlyList<IColumnInfo>)method.Invoke(helper, null)!;
        Assert.Empty(first);
        Assert.Same(first, second);
    }
    [Table("CacheTest")]
    private class CacheTestEntity
    {
        [Id(writable: false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string? Name { get; set; }

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
        [Id(writable: false)]
        [Column("Id", DbType.Int32)]
        [NonInsertable]
        public int Id { get; set; }
    }

    [Table("OnlyNonUpdate")]
    private class OnlyNonUpdateEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("NoUpdate", DbType.String)]
        [NonUpdateable]
        public string? NoUpdate { get; set; }
    }
}
