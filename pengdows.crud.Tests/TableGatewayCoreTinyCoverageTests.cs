using System;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("SqliteSerial")]
public class TableGatewayCoreTinyCoverageTests : SqlLiteContextTestBase
{
    [Fact]
    public void ReflectionPaths_CoverContextAccessor_AndVersionParsingWhitespaceBranch()
    {
        var gateway = new TableGateway<TestEntity, int>(Context, AuditValueResolver);

        var contextProperty = typeof(TableGateway<TestEntity, int>)
            .GetProperty("Context", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Context property not found.");

        var resolvedContext = contextProperty.GetValue(gateway);
        Assert.Same(Context, resolvedContext);

        var parseMethod = typeof(TableGateway<TestEntity, int>)
            .GetMethod("TryParseMajorVersion", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TryParseMajorVersion method not found.");

        var args = new object?[] { "   ", 0 };
        var parsed = (bool)parseMethod.Invoke(null, args)!;

        Assert.False(parsed);
    }
}
