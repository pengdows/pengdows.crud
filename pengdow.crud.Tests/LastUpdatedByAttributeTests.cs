#region

using System;
using System.Reflection;
using pengdow.crud.attributes;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

public class LastUpdatedByAttributeTests
{
    [Fact]
    public void Should_OnlyBeAllowed_OnProperties()
    {
        var usage = typeof(LastUpdatedByAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property));
        Assert.False(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.False(usage.AllowMultiple); // single use only
        Assert.True(usage.Inherited);
    }

    [Fact]
    public void UpdatedBy_ShouldHave_LastUpdatedByAttribute()
    {
        var prop = typeof(TestTable).GetProperty("UpdatedBy");

        var attr = prop?.GetCustomAttribute<LastUpdatedByAttribute>();
        Assert.NotNull(attr);
    }
}
