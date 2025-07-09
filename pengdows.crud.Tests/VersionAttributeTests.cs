#region

using System;
using System.Reflection;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class VersionAttributeTests
{
    [Fact]
    public void Should_OnlyBeAllowed_OnProperties()
    {
        var usage = typeof(VersionAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property));
        Assert.False(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.False(usage.AllowMultiple); // single use only
        Assert.True(usage.Inherited);
    }

    [Fact]
    public void Version_ShouldHave_VersionAttribute()
    {
        var prop = typeof(IdentityTestEntity).GetProperty("Version");

        var attr = prop?.GetCustomAttribute<VersionAttribute>();
        Assert.NotNull(attr);
    }
}
