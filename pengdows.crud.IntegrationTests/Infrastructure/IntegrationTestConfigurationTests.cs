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
    public void GetEnabledProviders_ExcludesFlatFile_ByDefault()
    {
        // FlatFile is intentionally off the always-on list (see BaseProviders' own comment) -
        // its per-test-file DDL coverage is still incomplete, and one provider failing the whole
        // cross-provider RunTestAgainstAllProvidersAsync aggregate would break every unrelated
        // test class, not just FlatFile's own row.
        var providers = IntegrationTestConfiguration.GetEnabledProviders(includeSnowflake: false);

        Assert.DoesNotContain(SupportedDatabase.FlatFile, providers);
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
    public void FilterIntegrationOnly_MatchesFlatFile_EvenWhenNotInBaseCandidateList()
    {
        // INTEGRATION_ONLY=FlatFile must work to test it directly (per BaseProviders' own comment)
        // even though FlatFile is deliberately excluded from the default candidate list - the
        // EnabledProviders pipeline is responsible for adding it to the candidate list first
        // (via ShouldIncludeFlatFile/GetEnabledProviders) before this filter ever runs; this test
        // locks down that FilterIntegrationOnly itself still matches it once it's a candidate.
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
