using Xunit;

namespace pengdows.crud.Tests;

public sealed class StrategyVisibilityTests
{
    [Theory]
    [InlineData("pengdows.crud.strategies.connection.StandardConnectionStrategy")]
    [InlineData("pengdows.crud.strategies.connection.KeepAliveConnectionStrategy")]
    public void ConnectionStrategies_AreNotPublic(string typeName)
    {
        var assembly = typeof(DatabaseContext).Assembly;
        var type = assembly.GetType(typeName, false);

        Assert.NotNull(type);
        Assert.True(type!.IsNotPublic, $"{typeName} should not be public.");
    }
}