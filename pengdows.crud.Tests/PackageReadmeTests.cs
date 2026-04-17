using System;
using System.IO;
using Xunit;

namespace pengdows.crud.Tests;

public class PackageReadmeTests
{
    [Fact]
    public void PackageReadmeBuildBadgeTargetsExistingWorkflow()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string packageReadmePath = Path.Combine(repoRoot, "pengdows.crud", "README.md");
        string workflowPath = Path.Combine(repoRoot, ".github", "workflows", "deploy.yml");

        string readme = File.ReadAllText(packageReadmePath);

        Assert.True(File.Exists(workflowPath), $"Expected workflow file to exist at '{workflowPath}'.");
        Assert.Contains("actions/workflows/deploy.yml/badge.svg", readme);
        Assert.DoesNotContain("actions/workflows/build.yml/badge.svg", readme);
    }
}
