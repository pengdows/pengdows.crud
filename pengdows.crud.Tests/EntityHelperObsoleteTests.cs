using System;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

public class TableGatewayObsoleteTests
{
    [Fact]
    public void TableGateway_IsNotObsolete()
    {
        var attr = GetObsoleteAttribute();

        Assert.Null(attr);
    }

    private static ObsoleteAttribute? GetObsoleteAttribute()
    {
        return typeof(TableGateway<DummyEntity, int>).GetCustomAttribute<ObsoleteAttribute>();
    }

    private sealed class DummyEntity
    {
        public int Id { get; set; }
    }
}
