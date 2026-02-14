using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class DatabaseContextParameterPoolRemovalTests
{
    [Fact]
    public void DatabaseContext_DoesNotExposeParameterPool()
    {
        var type = typeof(DatabaseContext);

        Assert.Null(type.GetField("_parameterPool", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Null(type.GetMethod("RentParameters", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Null(type.GetMethod("ReturnParameters", BindingFlags.Instance | BindingFlags.NonPublic));
    }
}
