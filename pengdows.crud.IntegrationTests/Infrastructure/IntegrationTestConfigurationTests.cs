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

    // Access (Windows-only, no Docker image) is added only when the caller asks (ShouldIncludeAccess supplies OperatingSystem.IsWindows()).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetEnabledProviders_IncludesAccess_OnlyWhenRequested(bool includeAccess)
    {
        var providers = IntegrationTestConfiguration.GetEnabledProviders(
            includeSnowflake: false, includeAccess: includeAccess);

        Assert.Equal(includeAccess, providers.Contains(SupportedDatabase.Access));
    }

    // Access is on exactly when the OS is Windows (ACE OLE DB + ADOX); no env var involved.
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("true")]
    public void ShouldIncludeAccess_FollowsTheOperatingSystem_IgnoringIncludeAccess(string? envValue)
    {
        var original = Environment.GetEnvironmentVariable("INCLUDE_ACCESS");
        try
        {
            Environment.SetEnvironmentVariable("INCLUDE_ACCESS", envValue);

            Assert.Equal(OperatingSystem.IsWindows(), IntegrationTestConfiguration.ShouldIncludeAccess);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INCLUDE_ACCESS", original);
        }
    }

    [Fact]
    public void GetEnabledProviders_AlwaysIncludesOracle()
    {
        // Oracle is always-on (gvenzl/oracle-free full-faststart starts reliably in Docker)
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

    // REV-061: BaseProviders listed FlatFile and Spanner twice. The fixture starts one container per
    // entry and keys them by database, so the second Spanner container replaced the first, which was
    // never disposed.
    [Fact]
    public void BaseProviders_ListEachDatabaseOnce()
    {
        var providers = IntegrationTestConfiguration.BaseProviders;

        Assert.Equal(providers.Count, providers.Distinct().Count());
    }

    [Fact]
    public void EnabledProviders_WithEveryOptIn_ListEachDatabaseOnce()
    {
        var providers = IntegrationTestConfiguration.GetEnabledProviders(true, true, true, true);

        Assert.Equal(providers.Count, providers.Distinct().Count());
        Assert.Contains(SupportedDatabase.Spanner, providers);
        Assert.Contains(SupportedDatabase.FlatFile, providers);
    }

    // run-integration-tests.sh runs the suite two databases at a time (one test process per batch,
    // so at most two databases' containers are up at once, as the testbed's dispatcher does); its
    // list of always-on databases must be BaseProviders, in order.
    [Fact]
    public void RunScriptBatches_AreTheAlwaysOnProviders()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "run-integration-tests.sh"));
        var match = System.Text.RegularExpressions.Regex.Match(script, @"always_on_databases=\(([^)]*)\)");
        Assert.True(match.Success, "run-integration-tests.sh declares no always_on_databases=(...) list");

        var listed = match.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(IntegrationTestConfiguration.BaseProviders.Select(p => p.ToString()), listed);

        // Every database it can schedule has a weight (expected seconds), longest first.
        foreach (var database in listed.Concat(new[] { "Snowflake", "SapHana", "InterBase", "Access" }))
        {
            Assert.Matches(@"\[" + database + @"\]=\d+", script);
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "pengdows.crud.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("pengdows.crud.sln not found above the test directory.");
    }
}
