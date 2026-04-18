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
        Assert.Contains("https://api.nuget.org/v3-flatcontainer/pengdows.stormgate/${VERSION}/${VERSION}.nupkg", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet nuget push pengdows.stormgate/bin/Release/pengdows.stormgate.${{ steps.version.outputs.version }}.nupkg \\", workflow, StringComparison.Ordinal);
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
        Assert.Contains(".github/coverage-baseline.stormgate.txt", workflow, StringComparison.Ordinal);
        Assert.Contains("stormgate_coverage: ${{ steps.stormgate_coverage.outputs.coverage }}", workflow, StringComparison.Ordinal);
        Assert.Contains("new_stormgate_coverage=\"${{ needs.build-and-test.outputs.stormgate_coverage }}\"", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishWorkflow_RatchetsCoverageUsingTrackedFiles_NotGitHubActionsVariables()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "deploy.yml");

        Assert.True(File.Exists(workflowPath), $"Workflow file not found: {workflowPath}");

        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains(".github/coverage-baseline.txt", workflow, StringComparison.Ordinal);
        Assert.Contains(".github/coverage-baseline.stormgate.txt", workflow, StringComparison.Ordinal);
        Assert.Contains(".github/coverage-baseline.opentelemetry.txt", workflow, StringComparison.Ordinal);
        Assert.Contains("current_baseline=$(tr -d '[:space:]' < .github/coverage-baseline.txt)", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("${{ vars.COVERAGE_BASELINE }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("${{ vars.COVERAGE_BASELINE_STORMGATE }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("${{ vars.COVERAGE_BASELINE_OPENTELEMETRY }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/variables", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CoverageBaselineFiles_AreTrackedByGit()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var gitIgnorePath = Path.Combine(repoRoot, ".gitignore");

        Assert.True(File.Exists(gitIgnorePath), $"Git ignore file not found: {gitIgnorePath}");

        var gitIgnore = File.ReadAllText(gitIgnorePath);

        Assert.DoesNotContain(".github/coverage-baseline.txt", gitIgnore, StringComparison.Ordinal);
        Assert.DoesNotContain(".github/coverage-baseline.stormgate.txt", gitIgnore, StringComparison.Ordinal);
        Assert.DoesNotContain(".github/coverage-baseline.opentelemetry.txt", gitIgnore, StringComparison.Ordinal);
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
