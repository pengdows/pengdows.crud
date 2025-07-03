#region
using System;
using System.Data;
using pengdows.crud.attributes;
using Xunit;
#endregion

namespace pengdows.crud.Tests;

public class ValidateRowIdTypeTests : SqlLiteContextTestBase
{
    public ValidateRowIdTypeTests()
    {
        TypeMap.Register<SimpleEntity>();
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(string))]
    [InlineData(typeof(int?))]
    [InlineData(typeof(Guid?))]
    public void Constructor_SupportedTypes_DoesNotThrow(Type idType)
    {
        var helperType = typeof(EntityHelper<SimpleEntity,int>).MakeGenericType(idType);
        var helper = Activator.CreateInstance(helperType, Context);
        Assert.NotNull(helper);
    }

    [Fact]
    public void Constructor_UnsupportedType_Throws()
    {
        Assert.Throws<TypeInitializationException>(() => new EntityHelper<SimpleEntity, DateTime>(Context));
    }

    [Table("Simple")]
    private class SimpleEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }
    }
}
