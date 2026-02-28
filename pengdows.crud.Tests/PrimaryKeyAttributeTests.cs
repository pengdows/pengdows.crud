using System;
using System.Reflection;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

public class PrimaryKeyAttributeTests
{
    [Fact]
    public void Constructor_WithOrder_SetsOrder()
    {
        var attr = new PrimaryKeyAttribute(5);
        Assert.Equal(5, attr.Order);
    }

    [Fact]
    public void Constructor_Parameterless_SetsDefaultOrder()
    {
        var attr = new PrimaryKeyAttribute();
        Assert.Equal(0, attr.Order);
    }

    [Fact]
    public void AttributeUsage_IsForPropertiesOnly()
    {
        var usage = typeof(PrimaryKeyAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(usage);
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property));
        Assert.False(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.False(usage.AllowMultiple);
    }
}