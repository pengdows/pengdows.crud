using System;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

public class EntityHelperObsoleteTests
{
    [Fact]
    public void EntityHelper_IsMarkedObsolete_WithTableGatewayMessage()
    {
        var attr = GetObsoleteAttribute();

        Assert.NotNull(attr);
        Assert.Contains("TableGateway", attr!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntityHelper_ObsoleteAttribute_DoesNotBlockCompilation()
    {
        var attr = GetObsoleteAttribute();

        Assert.NotNull(attr);
        Assert.False(attr!.IsError);
    }

    private static ObsoleteAttribute? GetObsoleteAttribute()
    {
        return typeof(EntityHelper<DummyEntity, int>).GetCustomAttribute<ObsoleteAttribute>();
    }

    private sealed class DummyEntity
    {
        public int Id { get; set; }
    }
}
