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

    [Fact]
    public void GetEnabledProviders_AlwaysIncludesOracle()
    {
        // Oracle is always-on (gvenzl/oracle-free:slim starts reliably in Docker)
        var providers = IntegrationTestConfiguration.GetEnabledProviders(includeSnowflake: false);

        Assert.Contains(SupportedDatabase.Oracle, providers);
    }

    [Fact]
    public void GetEnabledProviders_IncludesFlatFile_ByDefault()
    {
        var providers = IntegrationTestConfiguration.GetEnabledProviders(includeSnowflake: false);

        Assert.Contains(SupportedDatabase.FlatFile, providers);
    }

    [Fact]
    public void GetEnabledProviders_IncludesFlatFile_WhenRequested()
    {
        var providers = IntegrationTestConfiguration.GetEnabledProviders(includeSnowflake: false, includeFlatFile: true);

        Assert.Contains(SupportedDatabase.FlatFile, providers);
    }

    [Theory]
    [InlineData("FlatFile", true)]
    [InlineData("flatfile", true)]
    [InlineData("Sqlite,FlatFile", true)]
    [InlineData("Sqlite", false)]
    [InlineData(null, false)]
    public void ShouldIncludeFlatFile_ReflectsIntegrationOnlyToken(string? integrationOnly, bool expected)
    {
        Assert.Equal(expected, IntegrationTestConfiguration.ComputeShouldIncludeFlatFile(integrationOnly));
    }

    [Fact]
    public void FilterIntegrationOnly_MatchesFlatFile()
    {
        var providers = new[] { SupportedDatabase.Sqlite, SupportedDatabase.FlatFile };

        var filtered = IntegrationTestConfiguration.FilterIntegrationOnly(providers, "FlatFile");

        Assert.Single(filtered);
        Assert.Contains(SupportedDatabase.FlatFile, filtered);
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
