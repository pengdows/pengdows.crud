#region
using System.Data;
using System.Reflection;
using pengdows.crud.attributes;
using Xunit;
#endregion

namespace pengdows.crud.Tests;

public class PrimaryKeyCacheTests : SqlLiteContextTestBase
{
    [Fact]
    public void PrimaryKeys_CachesList()
    {
        TypeMap.Register<CompositeKeyEntity>();
        var info = TypeMap.GetTableInfo<CompositeKeyEntity>();
        var first = info.PrimaryKeys;
        var second = info.PrimaryKeys;
        Assert.Equal(2, first.Count);
        Assert.Same(first, second);
    }

    [Fact]
    public void PrimaryKeys_NoKeys_ReturnsEmpty()
    {
        TypeMap.Register<IdOnlyEntity>();
        var info = TypeMap.GetTableInfo<IdOnlyEntity>();
        var first = info.PrimaryKeys;
        var second = info.PrimaryKeys;
        Assert.Empty(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetPrimaryKeys_NoKeys_Throws()
    {
        TypeMap.Register<IdOnlyEntity>();
        var helper = new EntityHelper<IdOnlyEntity, int>(Context);
        var method = typeof(EntityHelper<IdOnlyEntity, int>).GetMethod("GetPrimaryKeys", BindingFlags.NonPublic | BindingFlags.Instance);
        var ex = Assert.Throws<TargetInvocationException>(() => method!.Invoke(helper, null));
        Assert.Contains("No primary keys found", ex.InnerException?.Message);
    }

    [Table("IdOnly")]
    private class IdOnlyEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }
    }
}

