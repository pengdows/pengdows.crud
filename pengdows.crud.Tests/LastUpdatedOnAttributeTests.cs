#region

using System;
using System.Reflection;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class LastUpdatedOnAttributeTests
{
    [Fact]
    public void Should_OnlyBeAllowed_OnProperties()
    {
        var usage = typeof(LastUpdatedOnAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property));
        Assert.False(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.False(usage.AllowMultiple); // single use only
        Assert.True(usage.Inherited);
    }

    [Fact]
    public void UpdatedAt_ShouldHave_LastUpdatedOnAttribute()
    {
        var prop = typeof(TestTable).GetProperty("UpdatedAt");

        var attr = prop?.GetCustomAttribute<LastUpdatedOnAttribute>();
        Assert.NotNull(attr);
    }
}
