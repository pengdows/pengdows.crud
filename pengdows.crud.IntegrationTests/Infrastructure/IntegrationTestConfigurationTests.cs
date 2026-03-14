using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.IntegrationTests.Infrastructure;

public class IntegrationTestConfigurationTests
{
    [Fact]
    public void GetEnabledProviders_IncludesSnowflake_WhenRequested()
    {
        var providers = IntegrationTestConfiguration.GetEnabledProviders(includeOracle: false, includeSnowflake: true);

        Assert.Contains(SupportedDatabase.Snowflake, providers);
    }

    [Fact]
    public void GetEnabledProviders_ExcludesSnowflake_WhenNotRequested()
    {
        var providers = IntegrationTestConfiguration.GetEnabledProviders(includeOracle: false, includeSnowflake: false);

        Assert.DoesNotContain(SupportedDatabase.Snowflake, providers);
    }

    [Fact]
    public void GetEnabledProviders_IncludesOracle_WhenRequested()
    {
        var providers = IntegrationTestConfiguration.GetEnabledProviders(includeOracle: true, includeSnowflake: false);

        Assert.Contains(SupportedDatabase.Oracle, providers);
    }

    [Fact]
    public void GetEnabledProviders_ExcludesOracle_WhenNotRequested()
    {
        var providers = IntegrationTestConfiguration.GetEnabledProviders(includeOracle: false, includeSnowflake: false);

        Assert.DoesNotContain(SupportedDatabase.Oracle, providers);
    }

    [Fact]
    public void FilterIntegrationOnly_ReturnsMatchingProviders()
    {
        var providers = new[] { SupportedDatabase.Sqlite, SupportedDatabase.Snowflake };

        var filtered = IntegrationTestConfiguration.FilterIntegrationOnly(providers, "Snowflake");

        Assert.Single(filtered);
        Assert.Contains(SupportedDatabase.Snowflake, filtered);
    }

    [Fact]
    public void FilterIntegrationOnly_ThrowsWhenNoMatches()
    {
        var providers = new[] { SupportedDatabase.Sqlite };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            IntegrationTestConfiguration.FilterIntegrationOnly(providers, "Snowflake"));

        Assert.Contains("INTEGRATION_ONLY did not match", ex.Message);
    }
}
