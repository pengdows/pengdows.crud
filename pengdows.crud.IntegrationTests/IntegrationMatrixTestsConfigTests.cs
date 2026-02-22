namespace pengdows.crud.IntegrationTests;

public class IntegrationMatrixTestsConfigTests
{
    [Fact]
    public void ShouldIncludeSnowflake_ReturnsTrue_WhenEnvVarSet()
    {
        var original = Environment.GetEnvironmentVariable("INCLUDE_SNOWFLAKE");
        Environment.SetEnvironmentVariable("INCLUDE_SNOWFLAKE", "true");

        try
        {
            Assert.True(IntegrationMatrixTests.ShouldIncludeSnowflake());
        }
        finally
        {
            Environment.SetEnvironmentVariable("INCLUDE_SNOWFLAKE", original);
        }
    }

    [Fact]
    public void ShouldIncludeSnowflake_ReturnsFalse_WhenEnvVarMissing()
    {
        var original = Environment.GetEnvironmentVariable("INCLUDE_SNOWFLAKE");
        Environment.SetEnvironmentVariable("INCLUDE_SNOWFLAKE", null);

        try
        {
            Assert.False(IntegrationMatrixTests.ShouldIncludeSnowflake());
        }
        finally
        {
            Environment.SetEnvironmentVariable("INCLUDE_SNOWFLAKE", original);
        }
    }
}
