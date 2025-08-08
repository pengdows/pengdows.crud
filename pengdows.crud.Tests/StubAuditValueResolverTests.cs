#region

using System;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class StubAuditValueResolverTests
{
    [Fact]
    public void Resolve_ReturnsConfiguredValues()
    {
        var resolver = new StubAuditValueResolver("test-user");
        var values = resolver.Resolve();

        Assert.Equal("test-user", values.UserId);
        Assert.True(values.UtcNow > DateTime.MinValue);
    }
}