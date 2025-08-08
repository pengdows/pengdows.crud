#region

using System;
using System.Reflection;
using pengdow.crud.attributes;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

public class EnumColumnAttributeTests : SqlLiteContextTestBase
{
    [Fact]
    public void Should_OnlyBeAllowed_OnProperties()
    {
        var usage = typeof(EnumColumnAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property));
        Assert.False(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.False(usage.AllowMultiple); // single use only
        Assert.True(usage.Inherited);
    }

    [Fact]
    public void ShouldHave_EnumColumnAttribute()
    {
        var prop = typeof(TestTable).GetProperty("Name");

        var attr = prop?.GetCustomAttribute<EnumColumnAttribute>();
        Assert.NotNull(attr);
    }
}