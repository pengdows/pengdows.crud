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
        Assert.Contains("pengdows.crud.abstractions/pengdows.crud.abstractions.csproj", contents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pengdows.crud/pengdows.crud.csproj", contents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pengdows.crud.fakeDb/pengdows.crud.fakeDb.csproj", contents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildScript_DoesNotIncludeIntegrationOrTestbedProjects()
    {
        var scriptPath = GetScriptPath();
        var contents = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("pengdows.crud.IntegrationTests", contents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("testbed", contents, StringComparison.OrdinalIgnoreCase);
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
