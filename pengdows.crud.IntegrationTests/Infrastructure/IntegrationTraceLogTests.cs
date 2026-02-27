using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

public sealed class IntegrationTraceLogTests
{
    [Fact]
    public void IsEnabled_ReturnsTrue_ForSnowflake_WhenSnowflakeDebugEnabled()
    {
        var original = Environment.GetEnvironmentVariable("SNOWFLAKE_DEBUG");
        Environment.SetEnvironmentVariable("SNOWFLAKE_DEBUG", "true");

        try
        {
            Assert.True(IntegrationTraceLog.IsEnabled(SupportedDatabase.Snowflake));
            Assert.False(IntegrationTraceLog.IsEnabled(SupportedDatabase.PostgreSql));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SNOWFLAKE_DEBUG", original);
        }
    }

    [Fact]
    public void IsEnabled_ReturnsTrue_ForAnyProvider_WhenGeneralTraceEnabled()
    {
        var original = Environment.GetEnvironmentVariable("INTEGRATION_TRACE");
        Environment.SetEnvironmentVariable("INTEGRATION_TRACE", "true");

        try
        {
            Assert.True(IntegrationTraceLog.IsEnabled(SupportedDatabase.Snowflake));
            Assert.True(IntegrationTraceLog.IsEnabled(SupportedDatabase.PostgreSql));
        }
        finally
        {
            Environment.SetEnvironmentVariable("INTEGRATION_TRACE", original);
        }
    }
}
