using System;
using System.IO;
using Xunit;

namespace pengdows.crud.Tests;

public class NuGetRestoreHygieneTests
{
    [Fact]
    public void RepoContainsTrackedLocalArtifactsPackageSourceDirectory()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string packageSourceDirectory = Path.Combine(repoRoot, "artifacts", "local-packages");
        string placeholderFile = Path.Combine(packageSourceDirectory, ".gitkeep");

        Assert.True(Directory.Exists(packageSourceDirectory),
            $"Expected local NuGet source directory to exist: {packageSourceDirectory}");
        Assert.True(File.Exists(placeholderFile),
            $"Expected tracked placeholder file to exist: {placeholderFile}");
    }
}
