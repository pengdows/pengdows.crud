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
