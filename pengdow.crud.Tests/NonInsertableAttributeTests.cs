using System;
using System.Reflection;
using pengdow.crud.attributes;
using Xunit;

namespace pengdow.crud.Tests;

public class NonInsertableAttributeTests
{
    [Fact]
    public void Should_OnlyBeAllowed_OnProperties()
    {
        var usage = typeof(NonInsertableAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property));
        Assert.False(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.False(usage.AllowMultiple);
        Assert.True(usage.Inherited);
    }

    [Fact]
    public void ShouldHave_NonInsertableAttribute()
    {
        var prop = typeof(NonInsertableIdEntity).GetProperty("Id");
        var attr = prop?.GetCustomAttribute<NonInsertableAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void TypeMap_Sets_IdIsWritableFalse_ForNonInsertableId()
    {
        var registry = new TypeMapRegistry();
        var info = registry.GetTableInfo<NonInsertableIdEntity>();
        Assert.NotNull(info.Id);
        Assert.False(info.Id!.IsIdIsWritable);
    }

    [Fact]
    public void TypeMap_Sets_NonInsertable_ForIdWritableFalse()
    {
        var registry = new TypeMapRegistry();
        var info = registry.GetTableInfo<IdentityTestEntity>();
        Assert.NotNull(info.Id);
        Assert.True(info.Id!.IsNonInsertable);
        Assert.False(info.Id.IsIdIsWritable);
    }
}
