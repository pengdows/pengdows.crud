#region

using System;
using System.Reflection;
using pengdow.crud.attributes;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

public class TableAttributeTests
{
    [Fact]
    public void Should_OnlyBeAllowed_OnClasses()
    {
        var usage = typeof(TableAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.False(usage.ValidOn.HasFlag(AttributeTargets.Property));
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.False(usage.AllowMultiple); // single use only
        Assert.False(usage.Inherited);
    }

    [Fact]
    public void Entity_ShouldHave_TableAttribute()
    {
        var attr = typeof(TestTable).GetCustomAttribute<TableAttribute>();

        Assert.NotNull(attr);
        Assert.Equal("test_table", attr.Name);
    }
}