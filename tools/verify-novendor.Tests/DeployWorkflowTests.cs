using System;
using System.IO;
using Xunit;

public class DeployWorkflowTests
{
    [Fact]
    public void PublishWorkflow_IncludesStormgateInPackCheckAndPushSteps()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "deploy.yml");

        Assert.True(File.Exists(workflowPath), $"Workflow file not found: {workflowPath}");

        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("dotnet pack pengdows.stormgate/pengdows.stormgate.csproj -c Release \\", workflow, StringComparison.Ordinal);
        Assert.Contains("https://api.nuget.org/v3-flatcontainer/pengdows.stormgate/${VERSION}/pengdows.stormgate.${VERSION}.nupkg", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet nuget push pengdows.stormgate/bin/Release/pengdows.stormgate.${{ steps.version.outputs.version }}.nupkg \\", workflow, StringComparison.Ordinal);
    }

    // The flat container serves a package at {id}/{version}/{id}.{version}.nupkg. A check that asks for
    // {id}/{version}/{version}.nupkg always gets a 404, so "already published" never fires (it did that in 2.0.x).
    [Fact]
    public void PublishWorkflow_AsksNuGetForEachPackageAtItsRealUrl()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "deploy.yml"));

        var matches = System.Text.RegularExpressions.Regex.Matches(workflow,
            @"v3-flatcontainer/(?<id>[A-Za-z.]+)/\$\{VERSION\}/(?<file>[^""]+)""");
        Assert.Equal(5, matches.Count);
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            Assert.Equal(match.Groups["id"].Value + ".${VERSION}.nupkg", match.Groups["file"].Value);
        }
    }

    // The release notes on GitHub are the version's CHANGELOG.md section, not a fixed "Automated release" line.
    [Fact]
    public void PublishWorkflow_UsesTheChangelogSectionAsTheReleaseNotes()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "deploy.yml"));

        Assert.Contains("CHANGELOG.md", workflow, StringComparison.Ordinal);
        Assert.Contains("body_path:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("body: Automated release", workflow, StringComparison.Ordinal);
    }

    // Everything after the push to NuGet and the release is bookkeeping. A failure there must not turn a published
    // release red (2.0.5's did), and an empty coverage value must not reach the comparison.
    [Fact]
    public void PublishWorkflow_CoverageRatchetCannotFailAPublishedRelease()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "deploy.yml"));

        var step = workflow.IndexOf("- name: Ratchet coverage baselines", StringComparison.Ordinal);
        Assert.True(step >= 0);
        var body = workflow.Substring(step, workflow.IndexOf("- name: Commit semver bump", step, StringComparison.Ordinal) - step);
        Assert.Contains("continue-on-error: true", body, StringComparison.Ordinal);
        Assert.Contains("-z \"$new_val\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishWorkflow_EnforcesAndRatchetsStormgateCoverage()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "deploy.yml");

        Assert.True(File.Exists(workflowPath), $"Workflow file not found: {workflowPath}");

        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("-targetdir:\"coverage-report-stormgate\"", workflow, StringComparison.Ordinal);
        Assert.Contains("-assemblyfilters:\"+pengdows.stormgate;-pengdows.stormgate.Tests\"", workflow, StringComparison.Ordinal);
        Assert.Contains("baseline=\"${{ vars.COVERAGE_BASELINE_STORMGATE }}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("stormgate_coverage: ${{ steps.stormgate_coverage.outputs.coverage }}", workflow, StringComparison.Ordinal);
        Assert.Contains("ratchet COVERAGE_BASELINE_STORMGATE \\", workflow, StringComparison.Ordinal);
        Assert.Contains("\"${{ needs.build-and-test.outputs.stormgate_coverage }}\" \\", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void StormgatePackage_UsesSharedRepositoryVersioning()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "deploy.yml");
        var csprojPath = Path.Combine(repoRoot, "pengdows.stormgate", "pengdows.stormgate.csproj");

        Assert.True(File.Exists(workflowPath), $"Workflow file not found: {workflowPath}");
        Assert.True(File.Exists(csprojPath), $"Project file not found: {csprojPath}");

        var workflow = File.ReadAllText(workflowPath);
        var csproj = File.ReadAllText(csprojPath);

        Assert.Contains("dotnet pack pengdows.stormgate/pengdows.stormgate.csproj -c Release \\", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:PackageVersion=${{ steps.version.outputs.version }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("<Version>", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("<AssemblyVersion>", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("<FileVersion>", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishWorkflow_BlocksPartialNuGetPublishesBeforeTaggingRelease()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "deploy.yml");

        Assert.True(File.Exists(workflowPath), $"Workflow file not found: {workflowPath}");

        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("any_published=false", workflow, StringComparison.Ordinal);
        Assert.Contains("elif [[ \"$any_published\" == \"true\" ]]; then", workflow, StringComparison.Ordinal);
        Assert.Contains("Version $VERSION is partially published. Resolve NuGet state before retrying.", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Version $VERSION is not fully published — proceeding (--skip-duplicate handles already-pushed packages).", workflow, StringComparison.Ordinal);
    }
}
