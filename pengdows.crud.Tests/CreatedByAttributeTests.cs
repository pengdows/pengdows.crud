#region

using System;
using System.Reflection;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class CreatedByAttributeTests
{
    [Fact]
    public void Should_OnlyBeAllowed_OnProperties()
    {
        var usage = typeof(CreatedByAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property));
        Assert.False(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.False(usage.AllowMultiple); // single use only
        Assert.True(usage.Inherited);
    }

    [Fact]
    public void CreatedBy_ShouldHave_CreatedByAttribute()
    {
        var prop = typeof(TestTable).GetProperty("CreatedBy");

        var attr = prop?.GetCustomAttribute<CreatedByAttribute>();
        Assert.NotNull(attr);
    }
}