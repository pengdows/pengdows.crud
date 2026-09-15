using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class SnowflakeDialectCapabilityTests
{
    private static SnowflakeDialect Dialect() =>
        new(new fakeDbFactory(SupportedDatabase.Snowflake), NullLogger<SnowflakeDialect>.Instance);

    [Fact]
    public void SupportsWindowFunctions_IsTrue()
        => Assert.True(Dialect().SupportsWindowFunctions);

    [Fact]
    public void SupportsCommonTableExpressions_IsTrue()
        => Assert.True(Dialect().SupportsCommonTableExpressions);

    [Fact]
    public void SupportsArrayTypes_IsTrue()
        => Assert.True(Dialect().SupportsArrayTypes);

    [Fact]
    public void SupportsJsonTypes_IsTrue()
        => Assert.True(Dialect().SupportsJsonTypes);
}
