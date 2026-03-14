namespace pengdows.crud.IntegrationTests;

public class IntegrationMatrixTestsConfigTests
{
    [Fact]
    public void ShouldIncludeOracle_ReturnsTrue_WhenEnvVarSet()
    {
        var original = Environment.GetEnvironmentVariable("INCLUDE_ORACLE");
        Environment.SetEnvironmentVariable("INCLUDE_ORACLE", "true");

        try
        {
            Assert.True(IntegrationMatrixTests.ShouldIncludeOracle());
        }
        finally
        {
            Environment.SetEnvironmentVariable("INCLUDE_ORACLE", original);
        }
    }

    [Fact]
    public void ShouldIncludeOracle_ReturnsFalse_WhenEnvVarMissing()
    {
        var original = Environment.GetEnvironmentVariable("INCLUDE_ORACLE");
        Environment.SetEnvironmentVariable("INCLUDE_ORACLE", null);

        try
        {
            Assert.False(IntegrationMatrixTests.ShouldIncludeOracle());
        }
        finally
        {
            Environment.SetEnvironmentVariable("INCLUDE_ORACLE", original);
        }
    }

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
