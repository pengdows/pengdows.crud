using System.Xml.Linq;
using Xunit;

namespace pengdows.crud.analyzers.Tests;

public sealed class AnalyzerNuGetDeploymentTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void AnalyzerProject_IsPackableWithExpectedPackageId()
    {
        var projectPath = Path.Combine(
            RepositoryRoot,
            "tools",
            "pengdows.crud.analyzers",
            "pengdows.crud.analyzers.csproj");

        var project = XDocument.Load(projectPath);

        Assert.Equal(
            "pengdows.crud.analyzers",
            project.Root?.Descendants("PackageId").Single().Value);
        Assert.Equal(
            "true",
            project.Root?.Descendants("IsPackable").Single().Value);
    }

    [Fact]
    public void DeployWorkflow_PacksChecksAndPushesAnalyzerPackage()
    {
        var workflowPath = Path.Combine(RepositoryRoot, ".github", "workflows", "deploy.yml");
        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains(
            "dotnet pack tools/pengdows.crud.analyzers/pengdows.crud.analyzers.csproj -c Release",
            workflow);
        Assert.Contains(
            "https://api.nuget.org/v3-flatcontainer/pengdows.crud.analyzers/${VERSION}/${VERSION}.nupkg",
            workflow);
        Assert.Contains(
            "dotnet nuget push tools/pengdows.crud.analyzers/bin/Release/pengdows.crud.analyzers.${{ steps.version.outputs.version }}.nupkg",
            workflow);
    }
}
