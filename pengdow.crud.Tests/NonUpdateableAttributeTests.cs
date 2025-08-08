#region

using System;
using System.Reflection;
using pengdow.crud.attributes;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

public class NonUpdateableAttributeTests
{
    [Fact]
    public void Should_OnlyBeAllowed_OnProperties()
    {
        var usage = typeof(NonUpdateableAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property));
        Assert.False(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.False(usage.AllowMultiple); // single use only
        Assert.True(usage.Inherited);
    }

    [Fact]
    public void ShouldHave_NonUpdateableAttribute()
    {
        var prop = typeof(TestTable).GetProperty("NonUpdateableColumn");

        var attr = prop?.GetCustomAttribute<NonUpdateableAttribute>();
        Assert.NotNull(attr);
    }
}