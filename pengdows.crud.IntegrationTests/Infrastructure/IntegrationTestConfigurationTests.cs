using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.IntegrationTests.Infrastructure;

public class IntegrationTestConfigurationTests
{
    [Fact]
    public void GetEnabledProviders_IncludesSnowflake_WhenRequested()
    {
        var providers = IntegrationTestConfiguration.GetEnabledProviders(includeSnowflake: true);

        Assert.Contains(SupportedDatabase.Snowflake, providers);
    }

    [Fact]
    public void GetEnabledProviders_ExcludesSnowflake_WhenNotRequested()
    {
        var providers = IntegrationTestConfiguration.GetEnabledProviders(includeSnowflake: false);

        Assert.DoesNotContain(SupportedDatabase.Snowflake, providers);
    }

    // SAP HANA (16-32 GB RAM) and InterBase (node-locked license, externally managed container) are
    // opt-in like Snowflake, via INCLUDE_SAPHANA / INCLUDE_INTERBASE, matching the testbed.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GetEnabledProviders_IncludesSapHanaAndInterBase_OnlyWhenRequested(bool includeSapHana, bool includeInterBase)
    {
        var providers = IntegrationTestConfiguration.GetEnabledProviders(
            includeSnowflake: false, includeSapHana: includeSapHana, includeInterBase: includeInterBase);

        Assert.Equal(includeSapHana, providers.Contains(SupportedDatabase.SapHana));
        Assert.Equal(includeInterBase, providers.Contains(SupportedDatabase.InterBase));
    }

    [Fact]
    public void GetEnabledProviders_AlwaysIncludesOracle()
    {
        // Oracle is always-on (gvenzl/oracle-free:slim starts reliably in Docker)
        var providers = IntegrationTestConfiguration.GetEnabledProviders(includeSnowflake: false);

        Assert.Contains(SupportedDatabase.Oracle, providers);
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
