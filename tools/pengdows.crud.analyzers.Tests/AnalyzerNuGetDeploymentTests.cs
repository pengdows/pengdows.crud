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
    public void AnalyzerProject_PacksTheMultiTenancyPropsFile()
    {
        // PGC027's opt-in enablement (PengdowsMultiTenancy) only reaches a consuming project's
        // compiler options if build/pengdows.crud.analyzers.props actually ships in the package —
        // NuGet's build/{PackageId}.props auto-import convention requires this exact path.
        var projectPath = Path.Combine(
            RepositoryRoot,
            "tools",
            "pengdows.crud.analyzers",
            "pengdows.crud.analyzers.csproj");
        var propsPath = Path.Combine(
            RepositoryRoot,
            "tools",
            "pengdows.crud.analyzers",
            "build",
            "pengdows.crud.analyzers.props");

        Assert.True(File.Exists(propsPath), $"Expected props file not found: {propsPath}");
        Assert.Contains("CompilerVisibleProperty", File.ReadAllText(propsPath));
        Assert.Contains("PengdowsMultiTenancy", File.ReadAllText(propsPath));

        var project = XDocument.Load(projectPath);
        var packedNoneItems = project.Root!.Descendants("None")
            .Where(e => e.Attribute("Pack")?.Value == "true")
            .ToArray();

        Assert.Contains(
            packedNoneItems,
            e => e.Attribute("Include")?.Value == "build/pengdows.crud.analyzers.props"
                 && e.Attribute("PackagePath")?.Value == "build/pengdows.crud.analyzers.props");
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
