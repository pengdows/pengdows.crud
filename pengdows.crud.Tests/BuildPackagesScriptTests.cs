using System;
using System.IO;
using Xunit;

namespace pengdows.crud.Tests;

public class BuildPackagesScriptTests
{
    [Fact]
    public void BuildScript_ListsAllRequiredPackages()
    {
        var scriptPath = GetScriptPath();
        var contents = File.ReadAllText(scriptPath);

        Assert.Contains("dotnet pack", contents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pengdows.crud.abstractions/pengdows.crud.abstractions.csproj", contents,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pengdows.crud/pengdows.crud.csproj", contents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pengdows.crud.fakeDb/pengdows.crud.fakeDb.csproj", contents,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pengdows.stormgate/pengdows.stormgate.csproj", contents,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildScript_DoesNotIncludeIntegrationOrTestbedProjects()
    {
        var scriptPath = GetScriptPath();
        var contents = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("pengdows.crud.IntegrationTests", contents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("testbed", contents, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // Version consistency — Directory.Build.props must declare 2.0.6
    // This branch briefly carried a stale VersionPrefix of 2.1.0 (left over from
    // backport/multitenancy-lease-fixes, predating this branch's own 2.0-partial-backport
    // merge) — corrected back to 2.0.6 since this line is the compatible patch successor to
    // 2.0.5, not a real 2.1.0 release; 2.1.0 is a separate branch. This test now locks in the
    // corrected value.
    // =========================================================================

    [Fact]
    public void DirectoryBuildProps_Version_Is_2_0_6()
    {
        var root = GetRepoRoot();
        var propsPath = Path.Combine(root, "Directory.Build.props");
        Assert.True(File.Exists(propsPath), $"Directory.Build.props not found at {propsPath}");

        var contents = File.ReadAllText(propsPath);
        Assert.Contains("<VersionPrefix>2.0.6</VersionPrefix>", contents, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>$(VersionPrefix).0</AssemblyVersion>", contents, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>$(VersionPrefix).0</FileVersion>", contents, StringComparison.Ordinal);
    }

    private static string GetScriptPath()
    {
        var root = GetRepoRoot();
        return Path.Combine(root, "build-packages.sh");
    }

    private static string GetRepoRoot()
    {
        var start = new DirectoryInfo(AppContext.BaseDirectory);
        for (var current = start; current != null; current = current.Parent)
        {
            var slnPath = Path.Combine(current.FullName, "pengdows.crud.sln");
            if (File.Exists(slnPath))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root for build script validation.");
    }
}
